using AssetUsage.Core.LoadIssues;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class BrokenNodeScanner
    {
        readonly EntityManager entityManager;
        readonly LoadIssueReport report;

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
                        report.AddBrokenNode(node.Index);
                    }
                }
            }
        }

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
