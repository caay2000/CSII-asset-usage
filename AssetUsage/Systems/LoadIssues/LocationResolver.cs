using Game.Areas;
using Game.Buildings;
using Game.Common;
using Unity.Entities;
using NetNode = Game.Net.Node;
using ObjectTransform = Game.Objects.Transform;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class LocationResolver
    {
        const int MaxDepth = 8;

        readonly EntityManager entityManager;

        public LocationResolver(EntityManager entityManager)
        {
            this.entityManager = entityManager;
        }

        public bool TryResolve(Entity entity, out Entity location)
        {
            var current = entity;
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (current == Entity.Null || !entityManager.Exists(current) || entityManager.HasComponent<Deleted>(current))
                {
                    break;
                }

                if (HasPosition(current))
                {
                    location = current;
                    return true;
                }

                if (entityManager.HasComponent<PropertyRenter>(current))
                {
                    current = entityManager.GetComponentData<PropertyRenter>(current).m_Property;
                }
                else if (entityManager.HasComponent<Owner>(current))
                {
                    current = entityManager.GetComponentData<Owner>(current).m_Owner;
                }
                else
                {
                    break;
                }
            }

            location = Entity.Null;
            return false;
        }

        bool HasPosition(Entity entity) =>
            entityManager.HasComponent<ObjectTransform>(entity)
            || entityManager.HasComponent<NetNode>(entity)
            || entityManager.HasComponent<Geometry>(entity);
    }
}
