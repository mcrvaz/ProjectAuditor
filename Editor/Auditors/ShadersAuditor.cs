using System;
using System.Collections.Generic;
using System.Linq;
using Unity.ProjectAuditor.Editor.Utils;
using UnityEditor;

namespace Unity.ProjectAuditor.Editor.Auditors
{
    public class ShadersAuditor : IAuditor
    {
        private static readonly ProblemDescriptor s_Descriptor = new ProblemDescriptor
            (
            302000,
            "Resources folder asset",
            Area.BuildSize,
            "The Resources folder is a common source of many problems in Unity projects. Improper use of the Resources folder can bloat the size of a project’s build, lead to uncontrollable excessive memory utilization, and significantly increase application startup times.",
            "Use AssetBundles when possible"
            );

        public IEnumerable<ProblemDescriptor> GetDescriptors()
        {
            // throw new NotImplementedException();
            yield return s_Descriptor;
        }

        public void Initialize(ProjectAuditorConfig config)
        {
            // throw new NotImplementedException();
        }

        public void Reload(string path)
        {
            // throw new NotImplementedException();
        }

        public void RegisterDescriptor(ProblemDescriptor descriptor)
        {
            // throw new NotImplementedException();
        }

        public void Audit(Action<ProjectIssue> onIssueFound, Action onComplete, IProgressBar progressBar = null)
        {
            // var allAssetPaths = AssetDatabase.GetAllAssetPaths();
            // // var allResources = allAssetPaths.Where(path => path.IndexOf("/resources/", StringComparison.OrdinalIgnoreCase) >= 0);
            // var allPlayerResources = allAssetPaths.Where(path => path.IndexOf("/editor/", StringComparison.OrdinalIgnoreCase) == -1);

            foreach (string guid in AssetDatabase.FindAssets("t:shader"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var issue = new ProjectIssue(s_Descriptor, assetPath, IssueCategory.Assets, new Location(assetPath, LocationType.Asset));
                onIssueFound(issue);
            }

            onComplete();
        }
    }
}
