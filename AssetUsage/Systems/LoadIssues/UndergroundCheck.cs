using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using ObjectElevation = Game.Objects.Elevation;

namespace AssetUsage.Systems.LoadIssues
{
    public static class UndergroundCheck
    {
        const Layer BuriedLayers = Layer.PowerlineLow | Layer.PowerlineHigh | Layer.WaterPipe | Layer.SewagePipe | Layer.StormwaterPipe | Layer.ResourceLine;
        const float TunnelLimitFactor = -2f;

        public static bool IsUnderground(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<Elevation>(entity))
            {
                return IsNetUnderground(entityManager, entity);
            }

            return entityManager.HasComponent<ObjectElevation>(entity)
                && entityManager.GetComponentData<ObjectElevation>(entity).m_Elevation < 0;
        }

        static bool IsNetUnderground(EntityManager entityManager, Entity entity)
        {
            float lowest = math.cmin(entityManager.GetComponentData<Elevation>(entity).m_Elevation);
            if (lowest >= 0)
            {
                return false;
            }

            if (!entityManager.HasComponent<PrefabRef>(entity))
            {
                return true;
            }

            var prefab = entityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (entityManager.HasComponent<NetData>(prefab)
                && (entityManager.GetComponentData<NetData>(prefab).m_RequiredLayers & BuriedLayers) != 0)
            {
                return true;
            }

            return !entityManager.HasComponent<NetGeometryData>(prefab)
                || lowest <= entityManager.GetComponentData<NetGeometryData>(prefab).m_ElevationLimit * TunnelLimitFactor;
        }
    }
}
