using TransferManagerCE.CustomManager;
using static TransferManagerCE.NetworkModeHelper;
using System.Collections.Generic;
using ColossalFramework;
using System;
using UnityEngine;
using SleepyCommon;

namespace TransferManagerCE
{
    public class NodeLinkGenerator
    {
        // Store local references to buffers for faster access
        protected readonly NetNode[] NetNodes = Singleton<NetManager>.instance.m_nodes.m_buffer;
        protected readonly NetSegment[] NetSegments = Singleton<NetManager>.instance.m_segments.m_buffer;
        protected readonly NetLane[] NetLanes = Singleton<NetManager>.instance.m_lanes.m_buffer;

        private ItemClass.Service m_service1;
        private ItemClass.Service m_service2;
        private ItemClass.Service m_service3;
        protected NetInfo.LaneType m_laneTypes;
        private VehicleInfo.VehicleType m_vehicleTypes;
        private bool m_bPedestrianZone;
        private bool m_bCargoPathAllowed;

        // Data to return
        private NodeLinkData m_nodeLinks = new NodeLinkData();
        private HashSet<ushort> m_nodes = new HashSet<ushort>();

        // ----------------------------------------------------------------------------------------
        public NodeLinkGenerator(NetworkMode mode) 
        {
            bool bGoodsMaterial = (mode == NetworkModeHelper.NetworkMode.Goods);
            PathDistanceTypes.GetService(bGoodsMaterial, out m_service1, out m_service2, out m_service3);
            m_laneTypes = PathDistanceTypes.GetLaneTypes(bGoodsMaterial);
            m_vehicleTypes = PathDistanceTypes.GetVehicleTypes(bGoodsMaterial);
            m_bPedestrianZone = (mode == NetworkModeHelper.NetworkMode.PedestrianZone);
            m_bCargoPathAllowed = bGoodsMaterial;
        }

        public NodeLinkData GetNodeLinks(ushort nodeId)
        {
            m_nodeLinks.Clear();
            m_nodes.Clear();

            NetNode node = NetNodes[nodeId];
            if (node.m_flags != 0 && IsNodeNetInfoValid(nodeId, node))
            {
                // Loop through segments to find neighboring roads
                for (int i = 0; i < 8; ++i)
                {
                    ushort segmentId = node.GetSegment(i);
                    if (segmentId != 0)
                    {
                        ProcessSegment(segmentId, nodeId);
                    }
                }

                // Loop through lanes to if see there are any extra connections segments
                ProcessLaneSegments(node.m_lane, nodeId);
            }

            return m_nodeLinks;
        }

        protected void AddNodeLink(ushort nodeId, float fTravelTime, NetInfo.Direction direction)
        {
            if (!m_nodes.Contains(nodeId))
            {
                m_nodeLinks.Add(nodeId, fTravelTime, direction, 0); // Normal node
                m_nodes.Add(nodeId);
            }
        }

        protected virtual void ProcessSegment(ushort segmentId, ushort usCurrentNodeId)
        {
            if (segmentId != 0)
            {
                NetSegment segment = NetSegments[segmentId];
                // Find direction of segment
                if (GetSegmentInfo(segmentId, segment, out NetInfo.Direction direction, out float fTravelTime))
                {
                    if (fTravelTime > 0)
                    {
                        NetInfo.Direction outgoingDirection = GetOutgoingDirection(segment, usCurrentNodeId, direction);
                        if (outgoingDirection != NetInfo.Direction.None)
                        {
                            // Build only legal outbound links from the current node.
                            if (segment.m_startNode != usCurrentNodeId && HasDirection(outgoingDirection, NetInfo.Direction.Backward))
                            {
                                AddNodeLink(segment.m_startNode, fTravelTime, NetInfo.Direction.Both);
                            }

                            if (segment.m_endNode != usCurrentNodeId && HasDirection(outgoingDirection, NetInfo.Direction.Forward))
                            {
                                AddNodeLink(segment.m_endNode, fTravelTime, NetInfo.Direction.Both);
                            }

                            // Only expose lane nodes that are reachable from this node.
                            ProcessLaneNodes(segment, usCurrentNodeId, segment.m_lanes, fTravelTime, outgoingDirection);
                        }
                    }
                }
            }
        }

        protected void ProcessLaneNodes(NetSegment segment, ushort usCurrentNodeId, uint laneId, float fTravelTime, NetInfo.Direction outgoingDirection)
        {
            // Loop through all sub nodes for this lane
            int iLaneLoopCount = 0;
            while (laneId != 0)
            {
                NetLane lane = NetLanes[laneId];
                if (lane.m_flags != 0 && IsLaneValid(segment, lane, outgoingDirection))
                {
                    int iNodeLoopCount = 0;
                    ushort nodeId = lane.m_nodes;
                    while (nodeId != 0)
                    {
                        NetNode node = NetNodes[nodeId];
                        if (nodeId != usCurrentNodeId && node.m_flags != 0 && IsNodeNetInfoValid(nodeId, node))
                        {
                            // Reachable lane nodes become ordinary outbound graph links.
                            AddNodeLink(nodeId, fTravelTime, NetInfo.Direction.Both);
                        }

                        nodeId = node.m_nextLaneNode;

                        // Safety check in case we get caught in an infinite loop somehow
                        if (iNodeLoopCount++ > NetManager.MAX_NODE_COUNT)
                        {
                            Debug.Log("Invalid node loop detected");
                            break;
                        }
                    }
                }

                // Update laneId
                laneId = lane.m_nextLane;

                // Safety check in case we get caught in an infinite loop somehow
                if (iLaneLoopCount++ > NetManager.MAX_LANE_COUNT)
                {
                    CDebug.Log("Invalid lane loop detected");
                    break;
                }
            }
        }

        private void ProcessLaneSegments(uint laneId, ushort nodeId)
        {
            // Loop through lanes to if see there are any extra connections segments
            int iLaneCount = 0;
            while (laneId != 0)
            {
                NetLane lane = NetLanes[laneId];
                if (lane.m_flags != 0 && lane.m_segment != 0)
                {
                    ProcessSegment(lane.m_segment, nodeId);
                }

                laneId = lane.m_nextLane;

                if (++iLaneCount >= 32768)
                {
                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Invalid list detected!\n" + Environment.StackTrace);
                    break;
                }
            }
        }

        public bool IsNodeNetInfoValid(ushort nodeId, NetNode node)
        {
            if ((node.m_flags & NetNode.Flags.LevelCrossing) != 0)
            {
                // These are a bit tricky as sometimes they have a connection class and other times not, so just let them through
                return true;
            }

            if (node.Info is not null && IsServiceValid(node.Info) && (node.Info.m_laneTypes & m_laneTypes) != 0)
            {
                if (m_bCargoPathAllowed && IsCargoStationPath(node.Info.GetService(), node.Info.GetAI()))
                {
                    // Check building is enabled.
                    ushort num = BuildingManager.instance.FindBuilding(node.m_position, 100f, ItemClass.Service.PublicTransport, ItemClass.SubService.None, Building.Flags.None, Building.Flags.None);
                    if (num != 0 && BuildingUtils.IsBuildingTurnedOff(num))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        public bool IsSegmentNetInfoValid(NetSegment segment)
        {
            return segment.Info is not null &&
                (segment.Info.m_laneTypes & m_laneTypes) != 0 &&
                IsServiceValid(segment.Info) &&
                IsNetInfoVehicleTypesValid(segment.Info) &&
                (m_bPedestrianZone || !segment.Info.IsPedestrianZoneRoad());
        }

        private bool IsServiceValid(NetInfo info)
        {
            ItemClass.Service service = info.GetService();

            if (service != ItemClass.Service.None)
            {
                if (service == ItemClass.Service.Beautification)
                {
                    return m_bCargoPathAllowed && IsCargoStationPath(service, info.GetAI());
                }
                else if (service == m_service1 || service == m_service2 || service == m_service3)
                {
                    return true;
                }
            }

            // Also check connection class for objects like dams, level crossings.
            ItemClass.Service connectionServices = info.GetConnectionClass().m_service;
            if (connectionServices != ItemClass.Service.None)
            {
                if (connectionServices == ItemClass.Service.Beautification)
                {
                    return m_bCargoPathAllowed && IsCargoStationPath(connectionServices, info.GetAI());
                }
                else if (connectionServices == m_service1 || connectionServices == m_service2 || connectionServices == m_service3)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsNetInfoVehicleTypesValid(NetInfo info)
        {
            if (m_bCargoPathAllowed && IsCargoStationPath(info.GetService(), info.GetAI()))
            {
                // Cargo stations seem to label their connector nodes as Beautification for some reason
                // and annoyingly their vehicle types are set to None so we need to handle this separately.
                return true;
            }
            else
            {
                return (info.m_vehicleTypes & m_vehicleTypes) != 0;
            }
        }

        private bool GetSegmentInfo(ushort segmentId, NetSegment segment, out NetInfo.Direction direction, out float fTravelTime)
        {
            fTravelTime = float.MaxValue;
            direction = NetInfo.Direction.None;

            // This code assumes all valid lanes have same speed, you could find fastest but would take longer
            if (segment.m_flags != 0)
            {
                NetInfo info = segment.Info;
                if (info is not null && IsSegmentNetInfoValid(segment))
                {
                    bool bForwards = false;
                    bool bBackwards = false;

                    // Cargo Station adjustment.
                    float fCargoTravelTimeAdjustment = 0.0f;
                    if (m_bCargoPathAllowed && IsCargoStationPath(info.GetService(), info.GetAI()))
                    {
                        fCargoTravelTimeAdjustment = SaveGameSettings.GetSettings().PathDistanceCargoStationDelay;
                    }

                    // Start from the last as the pedestrian lanes are at the start
                    for (int i = info.m_lanes.Length - 1; i >= 0; --i)
                    {
                        NetInfo.Lane lane = info.m_lanes[i];
                        if ((lane.m_laneType & m_laneTypes) != 0)
                        {
                            // Determine smallest travel time
                            float fEffectiveTravelTime = segment.m_averageLength / lane.m_speedLimit;
                            fTravelTime = Mathf.Min(fTravelTime, fEffectiveTravelTime) + fCargoTravelTimeAdjustment;

                            // Determine available directions
                            // Some segments have the Inverted flag set which means we have to go the other way
                            NetInfo.Direction laneDirection;
                            if ((segment.m_flags & NetSegment.Flags.Invert) != 0)
                            {
                                laneDirection = NetInfo.InvertDirection(lane.m_finalDirection);
                            }
                            else
                            {
                                laneDirection = lane.m_finalDirection;
                            }

                            switch (laneDirection)
                            {
                                case NetInfo.Direction.Forward:
                                case NetInfo.Direction.AvoidForward:
                                    {
                                        bForwards = true;
                                        break;
                                    }
                                case NetInfo.Direction.Backward:
                                case NetInfo.Direction.AvoidBackward:
                                    {
                                        bBackwards = true;
                                        break;
                                    }
                                case NetInfo.Direction.Both:
                                case NetInfo.Direction.AvoidBoth:
                                    {
                                        bForwards = true;
                                        bBackwards = true;
                                        break;
                                    }
                            }
                        }
                    }

                    if (bForwards && bBackwards)
                    {
                        direction = NetInfo.Direction.Both;
                    }
                    else if (bForwards)
                    {
                        direction = NetInfo.Direction.Forward;
                    }
                    else if (bBackwards)
                    {
                        direction = NetInfo.Direction.Backward;
                    }

                    return direction != NetInfo.Direction.None;
                }
            }

            return false;
        }

        private static NetInfo.Direction GetOutgoingDirection(NetSegment segment, ushort currentNodeId, NetInfo.Direction direction)
        {
            if (segment.m_startNode == currentNodeId)
            {
                return HasDirection(direction, NetInfo.Direction.Forward) ? NetInfo.Direction.Forward : NetInfo.Direction.None;
            }

            if (segment.m_endNode == currentNodeId)
            {
                return HasDirection(direction, NetInfo.Direction.Backward) ? NetInfo.Direction.Backward : NetInfo.Direction.None;
            }

            return direction;
        }

        private bool IsLaneValid(NetSegment segment, NetLane lane, NetInfo.Direction outgoingDirection)
        {
            if (lane.Info is null || (lane.Info.m_laneType & m_laneTypes) == 0)
            {
                return false;
            }

            NetInfo.Direction laneDirection = lane.m_finalDirection;
            if ((segment.m_flags & NetSegment.Flags.Invert) != 0)
            {
                laneDirection = NetInfo.InvertDirection(laneDirection);
            }

            return HasDirection(laneDirection, outgoingDirection);
        }

        private static bool HasDirection(NetInfo.Direction source, NetInfo.Direction required)
        {
            return required switch
            {
                NetInfo.Direction.Forward => source == NetInfo.Direction.Forward ||
                                             source == NetInfo.Direction.AvoidForward ||
                                             source == NetInfo.Direction.Both ||
                                             source == NetInfo.Direction.AvoidBoth,
                NetInfo.Direction.Backward => source == NetInfo.Direction.Backward ||
                                              source == NetInfo.Direction.AvoidBackward ||
                                              source == NetInfo.Direction.Both ||
                                              source == NetInfo.Direction.AvoidBoth,
                _ => source != NetInfo.Direction.None,
            };
        }

        // Cargo stations seem to label their connector nodes as Beautification for some reason
        // and annoyingly their vehicle types are set to None so we need to handle this separately.
        private static bool IsCargoStationPath(ItemClass.Service service, PrefabAI ai)
        {
            if (service == ItemClass.Service.Beautification)
            {
                // Check it actually is a cargo path
                switch (ai)
                {
                    case TransportPathAI:
                    case CargoPathAI:
                    case CanalAI: // Canals have an embedded ferry path
                        {
                            return true;
                        }
                }
            }
            return false;
        }
    }
}
