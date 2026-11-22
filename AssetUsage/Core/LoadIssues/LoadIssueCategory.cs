namespace AssetUsage.Core.LoadIssues
{
    public enum LoadIssueCategory
    {
        Content,
        Building,
        Object,
        Surface,
        Zone,
        Brand,
        Other,
    }

    public static class LoadIssueClassifier
    {
        public static LoadIssueCategory Classify(string prefabId)
        {
            int separator = prefabId?.IndexOf(':') ?? -1;
            string type = separator > 0 ? prefabId.Substring(0, separator) : prefabId;
            switch (type)
            {
                case "ContentPrefab":
                    return LoadIssueCategory.Content;
                case "BuildingPrefab":
                    return LoadIssueCategory.Building;
                case "StaticObjectPrefab":
                    return LoadIssueCategory.Object;
                case "SurfacePrefab":
                    return LoadIssueCategory.Surface;
                case "ZonePrefab":
                    return LoadIssueCategory.Zone;
                case "BrandPrefab":
                    return LoadIssueCategory.Brand;
                default:
                    return LoadIssueCategory.Other;
            }
        }
    }
}
