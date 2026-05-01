using KianCommons;
using SleepyCommon;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using static TransferManagerCE.NodeLinkData;

namespace TransferManagerCE
{
    internal class MiddleNodeBypass
    {
        const int iMIN_LINK_COUNT = 5;

        private Dictionary<ushort, NodeLinkData> m_data;
        private Dictionary<ushort, NodeLinkData> m_newLinks = new Dictionary<ushort, NodeLinkData>();
        private Dictionary<ushort, int> m_incomingCounts = new Dictionary<ushort, int>();

        public MiddleNodeBypass(Dictionary<ushort, NodeLinkData> data)
        {
            m_data = data;

        }

        public void Bypass()
        {
            BuildIncomingCounts();

            foreach (KeyValuePair<ushort, NodeLinkData> kvp in m_data)
            {
                ushort startNodeId = kvp.Key;
                NodeLinkData data = kvp.Value;
                if (!IsPassThroughNode(startNodeId, data))
                {
                    // this is a junction or a dead end, follow all middle nodes
                    foreach (NodeLink link in data.items)
                    {
                        float fTravelTime = link.m_fTravelTime;
                        int iNodeCount = 1;
                        HashSet<ushort> visitedNodes = new HashSet<ushort> { startNodeId };
                        FollowMiddleNodes(startNodeId, link.m_nodeId, startNodeId, link.m_nodeId, visitedNodes, ref fTravelTime, ref iNodeCount);
                    }
                }
            }

            // Replace node data in graph
            foreach (KeyValuePair<ushort, NodeLinkData> kvp in m_newLinks)
            {
                m_data[kvp.Key] = kvp.Value;
            }
        }

        public void FollowMiddleNodes(ushort startNodeId, ushort firstNode, ushort prevNode, ushort cuurentNodeId, HashSet<ushort> visitedNodes, ref float fTravelTime, ref int iNodeCount)
        {
            if (!visitedNodes.Add(cuurentNodeId))
            {
                return;
            }

            if (m_data.TryGetValue(cuurentNodeId, out NodeLinkData linkData))
            {
                if (linkData.Count == 2)
                {
                    // Keep following node
                    foreach (NodeLink link in linkData.items)
                    {
                        if (link.m_nodeId != prevNode)
                        {
                            fTravelTime += link.m_fTravelTime;
                            iNodeCount++;
                            FollowMiddleNodes(startNodeId, firstNode, cuurentNodeId, link.m_nodeId, visitedNodes, ref fTravelTime, ref iNodeCount);
                        }
                    }
                }
                else if (linkData.Count == 1 && GetIncomingCount(cuurentNodeId) == 1)
                {
                    NodeLink link = linkData.items[0];
                    if (link.m_nodeId != prevNode)
                    {
                        fTravelTime += link.m_fTravelTime;
                        iNodeCount++;
                        FollowMiddleNodes(startNodeId, firstNode, cuurentNodeId, link.m_nodeId, visitedNodes, ref fTravelTime, ref iNodeCount);
                    }
                }
                else
                {
                    // Last node
                    iNodeCount++;

                    if (iNodeCount >= iMIN_LINK_COUNT)
                    {
                        // Add link to new graph
                        if (!m_newLinks.TryGetValue(startNodeId, out NodeLinkData data))
                        {
                            data = new NodeLinkData(m_data[startNodeId]); // Take a copy as we cant change in place while looping
                        }
                        data.Add(new NodeLink(cuurentNodeId, fTravelTime, NetInfo.Direction.Both, firstNode));
                        m_newLinks[startNodeId] = data;
                    }
                }
            }
        }

        private void BuildIncomingCounts()
        {
            m_incomingCounts.Clear();
            foreach (KeyValuePair<ushort, NodeLinkData> kvp in m_data)
            {
                foreach (NodeLink link in kvp.Value.items)
                {
                    if (!m_incomingCounts.TryAdd(link.m_nodeId, 1))
                    {
                        m_incomingCounts[link.m_nodeId]++;
                    }
                }
            }
        }

        private bool IsPassThroughNode(ushort nodeId, NodeLinkData linkData)
        {
            return linkData.Count == 2 || (linkData.Count == 1 && GetIncomingCount(nodeId) == 1);
        }

        private int GetIncomingCount(ushort nodeId)
        {
            if (m_incomingCounts.TryGetValue(nodeId, out int count))
            {
                return count;
            }

            return 0;
        }
    }
}
