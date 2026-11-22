using System.Collections.Generic;
using AssetUsage.Core.LoadIssues;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class BrokenNodeScanner
    {
        readonly EntityManager entityManager;
        readonly LoadIssueReport report;
        readonly Dictionary<Entity, List<Entity>> edgesByNode = new Dictionary<Entity, List<Entity>>();

        public BrokenNodeScanner(EntityManager entityManager, LoadIssueReport report)
        {
            this.entityManager = entityManager;
            this.report = report;
        }

        public void Scan()
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Game.Net.ElectricityConnection>(),
                ComponentType.ReadOnly<ElectricityNodeConnection>(),
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<ConnectedNode>());
            using var edges = query.ToEntityArray(Allocator.TempJob);
            using var connections = query.ToComponentDataArray<ElectricityNodeConnection>(Allocator.TempJob);
            for (int i = 0; i < edges.Length; i++)
            {
                var connectedNodes = entityManager.GetBuffer<ConnectedNode>(edges[i], true);
                for (int j = 0; j < connectedNodes.Length; j++)
                {
                    var node = connectedNodes[j].m_Node;
                    if (IsBroken(node, connections[i].m_ElectricityNode))
                    {
                        Record(node, edges[i]);
                    }
                }
            }
        }

        public bool StillBroken(Entity node)
        {
            if (!edgesByNode.TryGetValue(node, out var edges))
            {
                return false;
            }

            foreach (var edge in edges)
            {
                if (IsConnected(edge, node)
                    && entityManager.HasComponent<ElectricityNodeConnection>(edge)
                    && IsBroken(node, entityManager.GetComponentData<ElectricityNodeConnection>(edge).m_ElectricityNode))
                {
                    return true;
                }
            }

            return false;
        }

        void Record(Entity node, Entity edge)
        {
            if (!edgesByNode.TryGetValue(node, out var edges))
            {
                edges = new List<Entity>();
                edgesByNode[node] = edges;
            }

            edges.Add(edge);
            report.AddBrokenNode(new CaseRef(node.Index, node.Version), KindOf(node));
        }

        bool IsConnected(Entity edge, Entity node)
        {
            if (!entityManager.Exists(edge) || !entityManager.HasBuffer<ConnectedNode>(edge))
            {
                return false;
            }

            var connectedNodes = entityManager.GetBuffer<ConnectedNode>(edge, true);
            for (int i = 0; i < connectedNodes.Length; i++)
            {
                if (connectedNodes[i].m_Node == node)
                {
                    return true;
                }
            }

            return false;
        }

        string KindOf(Entity node)
        {
            var prefab = entityManager.GetComponentData<PrefabRef>(node).m_Prefab;
            var layers = entityManager.HasComponent<NetData>(prefab) ? entityManager.GetComponentData<NetData>(prefab).m_RequiredLayers : Layer.None;
            string kind = BaseKind(layers);
            return IsUnderground(node) ? $"{kind} (underground)" : kind;
        }

        static string BaseKind(Layer layers)
        {
            if ((layers & Layer.PowerlineHigh) != 0)
            {
                return "High voltage line";
            }

            if ((layers & Layer.PowerlineLow) != 0)
            {
                return "Low voltage line";
            }

            if ((layers & Layer.WaterPipe) != 0)
            {
                return "Water pipe";
            }

            if ((layers & Layer.SewagePipe) != 0)
            {
                return "Sewage pipe";
            }

            if ((layers & (Layer.Road | Layer.PublicTransportRoad)) != 0)
            {
                return "Road";
            }

            if ((layers & (Layer.TrainTrack | Layer.TramTrack | Layer.SubwayTrack)) != 0)
            {
                return "Track";
            }

            return (layers & Layer.Pathway) != 0 ? "Pathway" : "Other";
        }

        bool IsUnderground(Entity node) =>
            entityManager.HasComponent<Elevation>(node) && math.cmin(entityManager.GetComponentData<Elevation>(node).m_Elevation) < 0;

        bool IsBroken(Entity node, Entity middle)
        {
            if (!entityManager.HasComponent<PrefabRef>(node) || !entityManager.HasComponent<ElectricityNodeConnection>(node))
            {
                return false;
            }

            var prefab = entityManager.GetComponentData<PrefabRef>(node).m_Prefab;
            if (!entityManager.HasComponent<ElectricityConnectionData>(prefab))
            {
                return false;
            }

            var start = entityManager.GetComponentData<ElectricityNodeConnection>(node).m_ElectricityNode;
            return !HasFlowEdge(start, middle);
        }

        bool HasFlowEdge(Entity start, Entity end)
        {
            if (!entityManager.HasBuffer<ConnectedFlowEdge>(start))
            {
                return false;
            }

            var flowEdges = entityManager.GetBuffer<ConnectedFlowEdge>(start, true);
            for (int i = 0; i < flowEdges.Length; i++)
            {
                var edge = flowEdges[i].m_Edge;
                if (!entityManager.HasComponent<ElectricityFlowEdge>(edge))
                {
                    continue;
                }

                var data = entityManager.GetComponentData<ElectricityFlowEdge>(edge);
                if (data.m_Start == start && data.m_End == end)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
