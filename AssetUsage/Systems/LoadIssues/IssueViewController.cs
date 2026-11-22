using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
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

        public bool PlayerIsIdle => toolSystem.activeTool is DefaultToolSystem;

        public void ShowBrokenNode(Entity node)
        {
            ApplyInfoview(FindInfoview(StatusOf(node)));
            ApplyUnderground(UndergroundCheck.IsUnderground(entityManager, node));
        }

        public void ShowCase(Entity location)
        {
            RestoreInfoview();
            ApplyUnderground(UndergroundCheck.IsUnderground(entityManager, location));
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
                if (infoview == null || !infoview.isValid || infoview.m_Editor)
                {
                    continue;
                }

                foreach (var info in infoview.m_Infomodes)
                {
                    if (info?.m_Mode != null
                        && prefabSystem.TryGetEntity(info.m_Mode, out var mode)
                        && entityManager.HasComponent<InfoviewNetStatusData>(mode)
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

        void ApplyUnderground(bool wanted)
        {
            if (!(toolSystem.activeTool is DefaultToolSystem tool))
            {
                return;
            }

            if (undergroundApplied && !tool.underground)
            {
                undergroundApplied = false;
            }

            if (wanted && !tool.underground)
            {
                tool.underground = true;
                undergroundApplied = true;
            }
            else if (!wanted && undergroundApplied)
            {
                tool.underground = false;
                undergroundApplied = false;
            }
        }
    }
}
