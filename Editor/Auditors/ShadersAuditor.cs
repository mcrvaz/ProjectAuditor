using System;
using System.Collections.Generic;
using System.Linq;
using Unity.ProjectAuditor.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.ProjectAuditor.Editor.Auditors
{
    public class ShadersAuditor : IAuditor
    {
        private static readonly ProblemDescriptor s_Descriptor = new ProblemDescriptor
            (
            302001,
            "Shader Asset",
            Area.BuildSize,
            "",
            ""
            );

        public IEnumerable<ProblemDescriptor> GetDescriptors()
        {
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
            foreach (string guid in AssetDatabase.FindAssets("t:shader"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadMainAssetAtPath(assetPath) as Shader;

                // TODO: display pass names, shader variants, etc...
                var issue = new ProjectIssue(s_Descriptor, shader.name, IssueCategory.Assets, new Location(assetPath, LocationType.Asset));
                onIssueFound(issue);
            }

            onComplete();
        }
    }
}
