using SleepyCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TransferManagerCE
{
    public static class BuildingSpawnPointsIntegration
    {
        private enum Availability
        {
            Unknown,
            Available,
            NotAvailable
        }

        public const int PointTypeSpawn = 1;
        public const int PointTypeUnspawn = 2;

        private const int OptionsNone = 0;
        private const int PositionTypeFinal = 2;
        private const int DiagnosticKeyLimit = 1024;

        private static readonly object s_initLock = new object();
        private static readonly object s_logLock = new object();
        private static readonly HashSet<string> s_loggedDiagnosticKeys = new HashSet<string>();
        private static Availability s_availability = Availability.Unknown;
        private static PropertyInfo? s_instanceProperty;
        private static MethodInfo? s_managerIndexer;
        private static PropertyInfo? s_pointsProperty;
        private static PropertyInfo? s_typeProperty;
        private static PropertyInfo? s_typeValueProperty;
        private static PropertyInfo? s_categoriesProperty;
        private static PropertyInfo? s_categoriesValueProperty;
        private static MethodInfo? s_getAbsoluteMethod;
        private static object? s_optionsNone;
        private static object? s_positionTypeFinal;

        public static void LogDiagnostic(string message)
        {
#if DEBUG
            WriteDiagnostic(message);
#endif
        }

        public static void LogDiagnosticOnce(string key, string message)
        {
#if DEBUG
            lock (s_logLock)
            {
                if (s_loggedDiagnosticKeys.Count >= DiagnosticKeyLimit || !s_loggedDiagnosticKeys.Add(key))
                {
                    return;
                }
            }

            WriteDiagnostic(message);
#endif
        }

        private static void WriteDiagnostic(string message)
        {
#if DEBUG
            try
            {
                string logsPath = Path.Combine(Application.dataPath, "Logs");
                Directory.CreateDirectory(logsPath);
                string logFile = Path.Combine(logsPath, "TransferManagerCE-BSP.log");
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                CDebug.Log($"BuildingSpawnPoints diagnostic logging failed: {ex.Message}");
            }
#endif
        }

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return s_availability == Availability.Available;
            }
        }

        public static bool TryGetPointPosition(
            ushort buildingId,
            VehicleInfo.VehicleCategory vehicleCategory,
            int pointType,
            ref Building building,
            out Vector3 pointPosition)
        {
            pointPosition = Vector3.zero;

            if (!IsAvailable)
            {
                LogDiagnosticOnce("unavailable", "TryGetPointPosition skipped: BuildingSpawnPoints integration is unavailable");
                return false;
            }

            try
            {
                object? manager = s_instanceProperty?.GetValue(null, null);
                if (manager is null || s_managerIndexer is null)
                {
                    LogDiagnosticOnce("missing-manager-indexer", $"TryGetPointPosition missing manager/indexer category={vehicleCategory} pointType={FormatPointType(pointType)}");
                    return false;
                }

                object? buildingData = s_managerIndexer.Invoke(manager, new object[] { buildingId, s_optionsNone! });
                if (buildingData is null || s_pointsProperty is null)
                {
                    return false;
                }

                object? pointsObject = s_pointsProperty.GetValue(buildingData, null);
                if (pointsObject is not IEnumerable points)
                {
                    LogDiagnosticOnce($"no-points-{buildingId}", $"TryGetPointPosition no points building={buildingId}");
                    return false;
                }

                int pointCount = 0;
                ulong category = Convert.ToUInt64(vehicleCategory);
                foreach (object point in points)
                {
                    pointCount++;
                    if (IsMatchingPoint(point, category, pointType) && TryGetAbsolutePosition(point, ref building, out pointPosition))
                    {
                        LogDiagnosticOnce($"match-{buildingId}-{vehicleCategory}-{pointType}", $"TryGetPointPosition matched building={buildingId} category={vehicleCategory} pointType={FormatPointType(pointType)} position={FormatVector(pointPosition)} pointsChecked={pointCount}");
                        return true;
                    }
                }

            }
            catch (Exception ex)
            {
#if DEBUG
                CDebug.Log($"BuildingSpawnPoints integration failed: {ex.Message}");
#endif
                LogDiagnosticOnce($"exception-{buildingId}-{vehicleCategory}-{pointType}-{ex.GetType().FullName}-{ex.Message}", $"TryGetPointPosition exception building={buildingId} category={vehicleCategory} pointType={FormatPointType(pointType)} error={ex.Message}");
            }

            pointPosition = Vector3.zero;
            return false;
        }

        internal static string FormatPointType(int pointType)
        {
            return pointType switch
            {
                PointTypeSpawn => "Spawn",
                PointTypeUnspawn => "Unspawn",
                _ => pointType.ToString(),
            };
        }

        internal static string FormatVector(Vector3 position)
        {
            return $"({position.x:F1}, {position.y:F1}, {position.z:F1})";
        }

        private static string FormatType(Type? type)
        {
            return type is null ? "missing" : $"{type.FullName} [{type.Assembly.GetName().Name}]";
        }

        private static string FormatMember(MemberInfo? member)
        {
            return member is null ? "missing" : member.Name;
        }

        private static bool IsMatchingPoint(object point, ulong category, int pointType)
        {
            object? typeWrapper = s_typeProperty?.GetValue(point, null);
            object? typeValue = s_typeValueProperty?.GetValue(typeWrapper, null);
            if (typeValue is null || (Convert.ToInt32(typeValue) & pointType) == 0)
            {
                return false;
            }

            object? categoriesWrapper = s_categoriesProperty?.GetValue(point, null);
            object? categoriesValue = s_categoriesValueProperty?.GetValue(categoriesWrapper, null);
            return categoriesValue is not null && (Convert.ToUInt64(categoriesValue) & category) != 0;
        }

        private static bool TryGetAbsolutePosition(object point, ref Building building, out Vector3 position)
        {
            position = Vector3.zero;
            if (s_getAbsoluteMethod is null)
            {
                return false;
            }

            object[] parameters = new object[] { building, null!, null!, s_positionTypeFinal! };
            s_getAbsoluteMethod.Invoke(point, parameters);

            if (parameters[1] is Vector3 absolutePosition)
            {
                position = absolutePosition;
                return true;
            }

            return false;
        }

        private static void EnsureInitialized()
        {
            if (s_availability != Availability.Unknown)
            {
                return;
            }

            lock (s_initLock)
            {
                if (s_availability != Availability.Unknown)
                {
                    return;
                }

                s_availability = TryInitialize() ? Availability.Available : Availability.NotAvailable;
            }
        }

        private static bool TryInitialize()
        {
            try
            {
                Type? managerType = Type.GetType("BuildingSpawnPoints.Manager, BuildingSpawnPoints") ?? FindType("BuildingSpawnPoints.Manager");
                if (managerType is null)
                {
                    LogDiagnostic("TryInitialize unavailable: managerType=missing");
                    return false;
                }

                Assembly managerAssembly = managerType.Assembly;
                Type? singletonManagerType = ResolveType(managerAssembly, "ModsCommon.SingletonManager`1")
                                             ?? ResolveType(managerAssembly, "ModsCommon.Utilities.SingletonManager`1");
                Type? buildingDataType = ResolveType(managerAssembly, "BuildingSpawnPoints.BuildingData");
                Type? spawnPointType = ResolveType(managerAssembly, "BuildingSpawnPoints.BuildingSpawnPoint");
                Type? optionsType = ResolveType(managerAssembly, "BuildingSpawnPoints.Options");
                Type? positionType = ResolveType(managerAssembly, "BuildingSpawnPoints.PositionType");
                if (singletonManagerType is null || buildingDataType is null || spawnPointType is null || optionsType is null || positionType is null)
                {
                    LogDiagnostic($"TryInitialize unavailable: managerType={FormatType(managerType)} singletonManagerType={FormatType(singletonManagerType)} buildingDataType={FormatType(buildingDataType)} spawnPointType={FormatType(spawnPointType)} optionsType={FormatType(optionsType)} positionType={FormatType(positionType)}");
                    return false;
                }

                Type singletonManager = singletonManagerType.MakeGenericType(managerType);

                s_instanceProperty = singletonManager.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                s_managerIndexer = managerType.GetMethod("get_Item", new Type[] { typeof(ushort), optionsType });
                s_pointsProperty = buildingDataType.GetProperty("Points", BindingFlags.Public | BindingFlags.Instance);
                s_typeProperty = spawnPointType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
                s_categoriesProperty = spawnPointType.GetProperty("Categories", BindingFlags.Public | BindingFlags.Instance);
                s_getAbsoluteMethod = spawnPointType.GetMethod("GetAbsolute", new Type[] { typeof(Building).MakeByRefType(), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType(), positionType });
                s_optionsNone = Enum.ToObject(optionsType, OptionsNone);
                s_positionTypeFinal = Enum.ToObject(positionType, PositionTypeFinal);

                s_typeValueProperty = s_typeProperty?.PropertyType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                s_categoriesValueProperty = s_categoriesProperty?.PropertyType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

                bool available = s_instanceProperty is not null &&
                                 s_managerIndexer is not null &&
                                 s_pointsProperty is not null &&
                                 s_typeProperty is not null &&
                                 s_typeValueProperty is not null &&
                                 s_categoriesProperty is not null &&
                                 s_categoriesValueProperty is not null &&
                                 s_getAbsoluteMethod is not null &&
                                 s_optionsNone is not null &&
                                 s_positionTypeFinal is not null;

                LogDiagnostic($"TryInitialize result={(available ? "available" : "not available")} managerType={FormatType(managerType)} singletonManagerType={FormatType(singletonManagerType)} instanceProperty={FormatMember(s_instanceProperty)} managerIndexer={FormatMember(s_managerIndexer)} pointsProperty={FormatMember(s_pointsProperty)} typeProperty={FormatMember(s_typeProperty)} typeValue={FormatMember(s_typeValueProperty)} categoriesProperty={FormatMember(s_categoriesProperty)} categoriesValue={FormatMember(s_categoriesValueProperty)} getAbsoluteMethod={FormatMember(s_getAbsoluteMethod)} optionsNone={(s_optionsNone is null ? "missing" : s_optionsNone.ToString())} positionTypeFinal={(s_positionTypeFinal is null ? "missing" : s_positionTypeFinal.ToString())}");
                return available;
            }
            catch (Exception ex)
            {
#if DEBUG
                CDebug.Log($"BuildingSpawnPoints integration initialization failed: {ex.Message}");
#endif
                LogDiagnostic($"TryInitialize exception error={ex.Message}");
                return false;
            }
        }

        private static Type? ResolveType(Assembly preferredAssembly, string fullName)
        {
            return preferredAssembly.GetType(fullName, false) ?? FindType(fullName);
        }

        private static Type? FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = assembly.GetType(fullName, false);
                if (type is not null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
