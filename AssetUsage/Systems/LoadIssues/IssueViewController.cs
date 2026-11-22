using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using NetElevation = Game.Net.Elevation;
using ObjectElevation = Game.Objects.Elevation;
using Voltage = Game.Prefabs.ElectricityConnection.Voltage;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class IssueViewController
    {
        readonly EntityManager entityManager;
        readonly ToolSystem toolSystem;
        readonly PrefabSystem prefabSystem;
        readonly InfoviewInitializeSystem infoviews;
        InfoviewPrefab applied;
        InfoviewPrefab previous;
        bool undergroundApplied;

        public IssueViewController(EntityManager entityManager, ToolSystem toolSystem, PrefabSystem prefabSystem, InfoviewInitializeSystem infoviews)
        {
            this.entityManager = entityManager;
            this.toolSystem = toolSystem;
            this.prefabSystem = prefabSystem;
            this.infoviews = infoviews;
        }

        public void ShowBrokenNode(Entity node)
        {
            ApplyInfoview(FindInfoview(StatusOf(node)));
            ApplyUnderground(IsUnderground(node));
        }

        public void ShowCase(Entity location)
        {
            RestoreInfoview();
            ApplyUnderground(IsUnderground(location));
        }

        public void Reset()
        {
            RestoreInfoview();
            ApplyUnderground(false);
        }

        NetStatusType StatusOf(Entity node)
        {
            var prefab = entityManager.GetComponentData<PrefabRef>(node).m_Prefab;
            return entityManager.HasComponent<ElectricityConnectionData>(prefab)
                && entityManager.GetComponentData<ElectricityConnectionData>(prefab).m_Voltage == Voltage.High
                    ? NetStatusType.HighVoltageFlow
                    : NetStatusType.LowVoltageFlow;
        }

        InfoviewPrefab FindInfoview(NetStatusType status)
        {
            foreach (var infoview in infoviews.infoviews)
            {
                foreach (var info in infoview.m_Infomodes)
                {
                    var mode = prefabSystem.GetEntity(info.m_Mode);
                    if (entityManager.HasComponent<InfoviewNetStatusData>(mode)
                        && entityManager.GetComponentData<InfoviewNetStatusData>(mode).m_Type == status)
                    {
                        return infoview;
                    }
                }
            }

            return null;
        }

        void ApplyInfoview(InfoviewPrefab infoview)
        {
            if (infoview == null)
            {
                RestoreInfoview();
                return;
            }

            if (applied == null)
            {
                previous = toolSystem.infoview;
            }

            applied = infoview;
            toolSystem.infoview = infoview;
        }

        void RestoreInfoview()
        {
            if (applied != null && toolSystem.infoview == applied)
            {
                toolSystem.infoview = previous;
            }

            applied = null;
            previous = null;
        }

        void ApplyUnderground(bool underground)
        {
            var tool = toolSystem.activeTool;
            if (underground && !undergroundApplied && tool != null && tool.allowUnderground && !tool.requireUnderground)
            {
                tool.SetUnderground(true);
                undergroundApplied = true;
            }
            else if (!underground && undergroundApplied)
            {
                tool?.SetUnderground(false);
                undergroundApplied = false;
            }
        }

        bool IsUnderground(Entity entity)
        {
            if (entityManager.HasComponent<NetElevation>(entity))
            {
                return math.cmin(entityManager.GetComponentData<NetElevation>(entity).m_Elevation) < 0;
            }

            return entityManager.HasComponent<ObjectElevation>(entity)
                && entityManager.GetComponentData<ObjectElevation>(entity).m_Elevation < 0;
        }
    }
}
