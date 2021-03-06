using System;
using System.Collections.Generic;
using System.Linq;
using Unity.ProjectAuditor.Editor.CodeAnalysis;
using Unity.ProjectAuditor.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.ProjectAuditor.Editor.UI
{
    class ProjectAuditorWindow : EditorWindow, IHasCustomMenu, IProjectIssueFilter
    {
        enum AnalysisState
        {
            NotStarted,
            InProgress,
            Completed,
            Valid
        }

        enum ExportMode
        {
            All = 0,
            Filtered = 1,
            Selected
        }

        public enum CustomProperty
        {
            Assembly = 0
        }

        const string NoIssueSelectedText = "No issue selected";
        const string AnalysisIsRequiredText = "Missing Data: Please Analyze";

        static readonly string[] AreaNames = Enum.GetNames(typeof(Area));

        readonly AnalysisViewDescriptor[] m_AnalysisViewDescriptors =
        {
            new AnalysisViewDescriptor
            {
                category = IssueCategory.Assets,
                name = "Assets",
                groupByDescription = true,
                descriptionWithIcon = true,
                showAssemblySelection = false,
                showCritical = false,
                showDependencyView = true,
                showRightPanels = true,
                dependencyViewGuiContent = new GUIContent("Asset Dependencies", "Asset Dependencies"),
                columnDescriptors = new[]
                {
                    IssueTable.Column.Description,
                    IssueTable.Column.FileType,
                    IssueTable.Column.Path
                },
                costumColumnStyles = new[]
                {
                    new ColumnStyle
                    {
                        Content = new GUIContent("Assembly", "Managed Assembly name"),
                        Width = 300,
                        MinWidth = 100
                    }
                },
                onDoubleClick = FocusOnAssetInProjectWindow,
                analyticsEvent = ProjectAuditorAnalytics.UIButton.Assets
            },
            new AnalysisViewDescriptor
            {
                category = IssueCategory.Code,
                name = "Code",
                groupByDescription = true,
                descriptionWithIcon = false,
                showAssemblySelection = true,
                showCritical = true,
                showDependencyView = true,
                showRightPanels = true,
                dependencyViewGuiContent = new GUIContent("Inverted Call Hierarchy", "Inverted Call Hierarchy"),
                columnDescriptors = new[]
                {
                    IssueTable.Column.Description,
                    IssueTable.Column.Priority,
                    IssueTable.Column.Area,
                    IssueTable.Column.Filename,
                    IssueTable.Column.Custom
                },
                costumColumnStyles = new[]
                {
                    new ColumnStyle
                    {
                        Content = new GUIContent("Assembly", "Managed Assembly name"),
                        Width = 300,
                        MinWidth = 100
                    }
                },
                onDoubleClick = OpenTextFile,
                onOpenDescriptor = OpenDescriptor,
                analyticsEvent = ProjectAuditorAnalytics.UIButton.ApiCalls
            },
            new AnalysisViewDescriptor
            {
                category = IssueCategory.ProjectSettings,
                name = "Project Settings",
                groupByDescription = false,
                descriptionWithIcon = false,
                showAssemblySelection = false,
                showCritical = false,
                showDependencyView = false,
                showRightPanels = true,
                columnDescriptors = new[]
                {
                    IssueTable.Column.Description,
                    IssueTable.Column.Area
                },
                onDoubleClick = OpenProjectSettings,
                analyticsEvent = ProjectAuditorAnalytics.UIButton.ProjectSettings
            }
        };

        string[] m_ModeNames;
        ProjectAuditor m_ProjectAuditor;
        bool m_ShouldRefresh;
        ProjectAuditorAnalytics.Analytic m_AnalyzeButtonAnalytic;

        // UI
        readonly List<AnalysisView> m_AnalysisViews = new List<AnalysisView>();
        TreeViewSelection m_AreaSelection;
        TreeViewSelection m_AssemblySelection;

        // Serialized fields
        [SerializeField]
        int m_ActiveModeIndex;
        [SerializeField]
        string m_AreaSelectionSummary;
        [SerializeField]
        string[] m_AssemblyNames;
        [SerializeField]
        string m_AssemblySelectionSummary;
        [SerializeField]
        bool m_DeveloperMode;
        [SerializeField]
        ProjectReport m_ProjectReport;
        [SerializeField]
        TextFilter m_TextFilter;
        [SerializeField] AnalysisState m_AnalysisState = AnalysisState.NotStarted;
        [SerializeField]
        Preferences m_Preferences = new Preferences();

        AnalysisView m_ActiveAnalysisView
        {
            get { return m_AnalysisViews[m_ActiveModeIndex]; }
        }

        IssueTable m_ActiveIssueTable
        {
            get { return m_ActiveAnalysisView.table; }
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(Styles.DeveloperMode, m_DeveloperMode, OnToggleDeveloperMode);
            menu.AddItem(Styles.UserMode, !m_DeveloperMode, OnToggleDeveloperMode);
        }

        public bool Match(ProjectIssue issue)
        {
            // return false if the issue does not match one of these criteria:
            // - assembly name, if applicable
            // - area
            // - is not muted, if enabled
            // - critical context, if enabled/applicable

            Profiler.BeginSample("MatchAssembly");
            var matchAssembly = !m_ActiveAnalysisView.desc.showAssemblySelection ||
                m_AssemblySelection != null &&
                (m_AssemblySelection.Contains(issue.GetCustomProperty((int)CustomProperty.Assembly)) ||
                    m_AssemblySelection.ContainsGroup("All"));
            Profiler.EndSample();
            if (!matchAssembly)
                return false;

            Profiler.BeginSample("MatchArea");
            var matchArea = m_AreaSelection.Contains(issue.descriptor.area) ||
                m_AreaSelection.ContainsGroup("All");
            Profiler.EndSample();
            if (!matchArea)
                return false;

            if (!m_Preferences.mutedIssues)
            {
                Profiler.BeginSample("IsMuted");
                var muted = m_ProjectAuditor.config.GetAction(issue.descriptor, issue.callingMethod) ==
                    Rule.Action.None;
                Profiler.EndSample();
                if (muted)
                    return false;
            }

            if (m_ActiveAnalysisView.desc.showCritical &&
                m_Preferences.onlyCriticalIssues &&
                !issue.isPerfCriticalContext)
                return false;

            return m_TextFilter.Match(issue);
        }

        void OnEnable()
        {
            ProjectAuditorAnalytics.EnableAnalytics();

            m_ProjectAuditor = new ProjectAuditor();

            UpdateAssemblySelection();

            if (m_AreaSelection == null)
            {
                m_AreaSelection = new TreeViewSelection();
                if (!string.IsNullOrEmpty(m_AreaSelectionSummary))
                {
                    if (m_AreaSelectionSummary == "All")
                    {
                        m_AreaSelection.SetAll(AreaNames);
                    }
                    else if (m_AreaSelectionSummary != "None")
                    {
                        var areas = m_AreaSelectionSummary.Split(new[] {", "}, StringSplitOptions.None);
                        foreach (var area in areas) m_AreaSelection.selection.Add(area);
                    }
                }
                else
                {
                    m_AreaSelection.SetAll(AreaNames);
                }
            }

            m_ModeNames = m_AnalysisViewDescriptors.Select(m => m.name).ToArray();

            if (m_TextFilter == null)
                m_TextFilter = new TextFilter();

            m_AnalysisViews.Clear();
            foreach (var desc in m_AnalysisViewDescriptors)
            {
                var view = new AnalysisView(desc, m_ProjectAuditor.config, this);
                view.CreateTable(m_Preferences);

                if (m_AnalysisState == AnalysisState.Valid)
                    view.AddIssues(m_ProjectReport.GetIssues(view.desc.category));

                m_AnalysisViews.Add(view);
            }

            RefreshDisplay();
        }

        void OnGUI()
        {
            DrawSettings();
            DrawToolbar();
            if (IsAnalysisValid())
            {
                DrawMode();
                DrawFilters();
                DrawActions();

                if (m_ShouldRefresh || m_AnalysisState == AnalysisState.Completed)
                {
                    RefreshDisplay();
                    m_ShouldRefresh = false;
                }

                DrawIssues(); // and right-end panels
            }
            else
            {
                DrawHelpbox();
            }
        }

        void OnToggleDeveloperMode()
        {
            m_DeveloperMode = !m_DeveloperMode;
        }

        bool IsAnalysisValid()
        {
            return m_AnalysisState != AnalysisState.NotStarted;
        }

        void Analyze()
        {
            m_AnalyzeButtonAnalytic = ProjectAuditorAnalytics.BeginAnalytic();

            m_ShouldRefresh = true;
            m_AnalysisState = AnalysisState.InProgress;
            m_ProjectReport = new ProjectReport();
            foreach (var view in m_AnalysisViews)
            {
                view.table.Reset();
            }

            var newIssues = new List<ProjectIssue>();

            try
            {
                m_ProjectAuditor.Audit(projectIssue =>
                {
                    newIssues.Add(projectIssue);
                    m_ProjectReport.AddIssue(projectIssue);
                },
                    completed =>
                    {
                        // add batch of issues
                        foreach (var view in m_AnalysisViews)
                        {
                            view.AddIssues(newIssues);
                        }

                        newIssues.Clear();

                        if (completed)
                        {
                            m_AnalysisState = AnalysisState.Completed;
                        }

                        m_ShouldRefresh = true;
                    },
                    new ProgressBarDisplay());
            }
            catch (AssemblyCompilationException e)
            {
                m_AnalysisState = AnalysisState.NotStarted;
                Debug.LogError(e);
            }
        }

        void RefreshDisplay()
        {
            if (!IsAnalysisValid())
                return;

            if (m_AnalysisState == AnalysisState.Completed)
            {
                // update list of assembly names
                var scriptIssues = m_ProjectReport.GetIssues(IssueCategory.Code);
                m_AssemblyNames = scriptIssues.Select(i => i.GetCustomProperty((int)CustomProperty.Assembly)).Distinct().OrderBy(str => str).ToArray();
                UpdateAssemblySelection();

                m_AnalysisState = AnalysisState.Valid;

                ProjectAuditorAnalytics.SendUIButtonEventWithAnalyzeSummary(ProjectAuditorAnalytics.UIButton.Analyze,
                    m_AnalyzeButtonAnalytic, m_ProjectReport);
            }

            m_ActiveIssueTable.Reload();
        }

        void Reload()
        {
            OnEnable();
        }

        static bool ButtonWithDropdownList(GUIContent content, string[] buttonNames, GenericMenu.MenuFunction2 callback, params GUILayoutOption[] options)
        {
            var style = GUI.skin.FindStyle("DropDownButton");
            var rect = GUILayoutUtility.GetRect(content, style, options);

            var dropDownRect = rect;
            const float kDropDownButtonWidth = 20f;
            dropDownRect.xMin = dropDownRect.xMax - kDropDownButtonWidth;

            if (Event.current.type == EventType.MouseDown && dropDownRect.Contains(Event.current.mousePosition))
            {
                var menu = new GenericMenu();
                for (int i = 0; i != buttonNames.Length; i++)
                    menu.AddItem(new GUIContent(buttonNames[i]), false, callback, i);

                menu.DropDown(rect);
                Event.current.Use();

                return false;
            }

            return GUI.Button(rect, content, style);
        }

        void ExportDropDownCallback(object data)
        {
            var mode = (ExportMode)data;
            switch (mode)
            {
                case ExportMode.All:
                    Export();
                    return;
                case ExportMode.Filtered:
                    Export(issue => { return Match(issue); });
                    return;
                case ExportMode.Selected:
                    var selectedItems = m_ActiveIssueTable.GetSelectedItems();
                    Export(issue =>
                    {
                        return selectedItems.Any(item => item.Find(issue));
                    });
                    return;
            }
        }

        void Export(Func<ProjectIssue, bool> match = null)
        {
            var analytic = ProjectAuditorAnalytics.BeginAnalytic();
            if (IsAnalysisValid())
            {
                var path = EditorUtility.SaveFilePanel("Save analysis CSV data", "", "project-auditor-report.csv",
                    "csv");
                if (path.Length != 0)
                {
                    m_ProjectReport.ExportToCSV(path, issue => m_ProjectAuditor.config.GetAction(issue.descriptor, issue.callingMethod) !=
                        Rule.Action.None && (match == null || match(issue)));
                }
            }
            ProjectAuditorAnalytics.SendUIButtonEvent(ProjectAuditorAnalytics.UIButton.Export, analytic);
        }

        void DrawIssues()
        {
            ProblemDescriptor problemDescriptor = null;
            var selectedItems = m_ActiveIssueTable.GetSelectedItems();
            var selectedDescriptors = selectedItems.Select(i => i.ProblemDescriptor);
            var selectedIssues = selectedItems.Select(i => i.ProjectIssue);
            // find out if all descriptors are the same
            var firstDescriptor = selectedDescriptors.FirstOrDefault();
            if (selectedDescriptors.Count() == selectedDescriptors.Count(d => d.id == firstDescriptor.id))
                problemDescriptor = firstDescriptor;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();

            m_ActiveAnalysisView.OnGUI();

            EditorGUILayout.EndVertical();

            if (m_ActiveAnalysisView.desc.showRightPanels)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(LayoutSize.FoldoutWidth));
                DrawFoldouts(problemDescriptor);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();

            if (m_ActiveAnalysisView.desc.showDependencyView)
            {
                ProjectIssue issue = null;
                if (selectedIssues.Count() == 1)
                {
                    issue = selectedIssues.First();
                }

                DependencyNode dependencies = null;
                if (issue != null && issue.dependencies != null)
                {
                    // skip self
                    //if (issue.dependencies.HasChildren())

                    if (issue.dependencies as CallTreeNode != null)
                        dependencies =  issue.dependencies.GetChild();
                    else
                        dependencies = issue.dependencies;
                }

                m_ActiveAnalysisView.dependencyView.SetRoot(dependencies);

                DrawDependencyView(issue, dependencies);
            }
        }

        void DrawFoldouts(ProblemDescriptor problemDescriptor)
        {
            DrawDetailsFoldout(problemDescriptor);
            DrawRecommendationFoldout(problemDescriptor);
        }

        bool BoldFoldout(bool toggle, GUIContent content)
        {
            var foldoutStyle = new GUIStyle(EditorStyles.foldout);
            foldoutStyle.fontStyle = FontStyle.Bold;
            return EditorGUILayout.Foldout(toggle, content, foldoutStyle);
        }

        void DrawDetailsFoldout(ProblemDescriptor problemDescriptor)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(LayoutSize.FoldoutWidth));

            m_Preferences.details = BoldFoldout(m_Preferences.details, Styles.DetailsFoldout);
            if (m_Preferences.details)
            {
                if (problemDescriptor != null)
                {
                    EditorStyles.textField.wordWrap = true;
                    GUILayout.TextArea(problemDescriptor.problem, GUILayout.MaxHeight(LayoutSize.FoldoutMaxHeight));
                }
                else
                {
                    EditorGUILayout.LabelField(NoIssueSelectedText);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawRecommendationFoldout(ProblemDescriptor problemDescriptor)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(LayoutSize.FoldoutWidth));

            m_Preferences.recommendation = BoldFoldout(m_Preferences.recommendation, Styles.RecommendationFoldout);
            if (m_Preferences.recommendation)
            {
                if (problemDescriptor != null)
                {
                    EditorStyles.textField.wordWrap = true;
                    GUILayout.TextArea(problemDescriptor.solution, GUILayout.MaxHeight(LayoutSize.FoldoutMaxHeight));
                }
                else
                {
                    EditorGUILayout.LabelField(NoIssueSelectedText);
                }
            }

            EditorGUILayout.EndVertical();
        }

        void DrawDependencyView(ProjectIssue issue, DependencyNode root)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(LayoutSize.DependencyViewHeight));

            m_Preferences.dependencies = BoldFoldout(m_Preferences.dependencies, m_ActiveAnalysisView.desc.dependencyViewGuiContent);
            if (m_Preferences.dependencies)
            {
                if (root != null)
                {
                    var r = EditorGUILayout.GetControlRect(GUILayout.ExpandHeight(true));

                    m_ActiveAnalysisView.dependencyView.OnGUI(r);
                }
                else if (issue != null)
                {
                    GUIStyle s = new GUIStyle(EditorStyles.textField);
                    s.normal.textColor = Color.yellow;
                    EditorGUILayout.LabelField(AnalysisIsRequiredText, s, GUILayout.MaxHeight(LayoutSize.FoldoutMaxHeight));
                }
                else
                {
                    EditorGUILayout.LabelField(NoIssueSelectedText);
                }
            }

            EditorGUILayout.EndVertical();
        }

        string GetSelectedAssembliesSummary()
        {
            if (m_AssemblyNames != null && m_AssemblyNames.Length > 0)
                return GetSelectedSummary(m_AssemblySelection, m_AssemblyNames);
            return string.Empty;
        }

        internal string GetSelectedAreasSummary()
        {
            return GetSelectedSummary(m_AreaSelection, AreaNames);
        }

        string GetSelectedSummary(TreeViewSelection selection, string[] names)
        {
            string[] selectedStrings = selection.GetSelectedStrings(names, true);
            int numStrings = selectedStrings.Length;

            if (numStrings == 0)
                return "None";

            if (numStrings == 1)
                return selectedStrings[0];

            return string.Join(", ", selectedStrings);
        }

        void DrawSelectedText(string text)
        {
#if UNITY_2019_1_OR_NEWER
            GUIStyle treeViewSelectionStyle = "TV Selection";
            GUIStyle backgroundStyle = new GUIStyle(treeViewSelectionStyle);

            GUIStyle treeViewLineStyle = "TV Line";
            GUIStyle textStyle = new GUIStyle(treeViewLineStyle);
#else
            var textStyle = GUI.skin.label;
#endif

            var content = new GUIContent(text, text);
            var size = textStyle.CalcSize(content);
            var rect = EditorGUILayout.GetControlRect(GUILayout.MaxWidth(size.x), GUILayout.Height(size.y));
            if (Event.current.type == EventType.Repaint)
            {
#if UNITY_2019_1_OR_NEWER
                backgroundStyle.Draw(rect, false, false, true, true);
#endif
                GUI.Label(rect, content, textStyle);
            }
        }

        void DrawAssemblyFilter()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Styles.AssemblyFilter, GUILayout.Width(LayoutSize.FilterOptionsLeftLabelWidth));

            var lastEnabled = GUI.enabled;

            GUI.enabled = m_AnalysisState == AnalysisState.Valid && !AssemblySelectionWindow.IsOpen() &&
                m_ActiveAnalysisView.desc.showAssemblySelection;
            if (GUILayout.Button(Styles.AssemblyFilterSelect, EditorStyles.miniButton,
                GUILayout.Width(LayoutSize.FilterOptionsEnumWidth)))
            {
                if (m_AssemblyNames != null && m_AssemblyNames.Length > 0)
                {
                    var analytic = ProjectAuditorAnalytics.BeginAnalytic();

                    // Note: Window auto closes as it loses focus so this isn't strictly required
                    if (AssemblySelectionWindow.IsOpen())
                    {
                        AssemblySelectionWindow.CloseAll();
                    }
                    else
                    {
                        var windowPosition =
                            new Vector2(Event.current.mousePosition.x + LayoutSize.FilterOptionsEnumWidth,
                                Event.current.mousePosition.y + GUI.skin.label.lineHeight);
                        var screenPosition = GUIUtility.GUIToScreenPoint(windowPosition);

                        AssemblySelectionWindow.Open(screenPosition.x, screenPosition.y, this, m_AssemblySelection,
                            m_AssemblyNames);
                    }

                    ProjectAuditorAnalytics.SendUIButtonEvent(ProjectAuditorAnalytics.UIButton.AssemblySelect,
                        analytic);
                }
            }

            GUI.enabled = lastEnabled;

            m_AssemblySelectionSummary = GetSelectedAssembliesSummary();
            DrawSelectedText(m_AssemblySelectionSummary);

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }

        // stephenm TODO - if AssemblySelectionWindow and AreaSelectionWindow end up sharing a common base class then
        // DrawAssemblyFilter() and DrawAreaFilter() can be made to call a common method and just pass the selection, names
        // and the type of window we want.
        void DrawAreaFilter()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Styles.AreaFilter, GUILayout.Width(LayoutSize.FilterOptionsLeftLabelWidth));

            if (AreaNames.Length > 0)
            {
                var lastEnabled = GUI.enabled;
                // stephenm TODO - We don't currently have any sense of when the Auditor is busy and should disallow user input
                var enabled = /*!IsAnalysisRunning() &&*/ !AreaSelectionWindow.IsOpen();
                GUI.enabled = enabled;
                if (GUILayout.Button(Styles.AreaFilterSelect, EditorStyles.miniButton,
                    GUILayout.Width(LayoutSize.FilterOptionsEnumWidth)))
                {
                    var analytic = ProjectAuditorAnalytics.BeginAnalytic();

                    // Note: Window auto closes as it loses focus so this isn't strictly required
                    if (AreaSelectionWindow.IsOpen())
                    {
                        AreaSelectionWindow.CloseAll();
                    }
                    else
                    {
                        var windowPosition =
                            new Vector2(Event.current.mousePosition.x + LayoutSize.FilterOptionsEnumWidth,
                                Event.current.mousePosition.y + GUI.skin.label.lineHeight);
                        var screenPosition = GUIUtility.GUIToScreenPoint(windowPosition);

                        AreaSelectionWindow.Open(screenPosition.x, screenPosition.y, this, m_AreaSelection,
                            AreaNames);
                    }

                    ProjectAuditorAnalytics.SendUIButtonEvent(ProjectAuditorAnalytics.UIButton.AreaSelect, analytic);
                }

                GUI.enabled = lastEnabled;

                m_AreaSelectionSummary = GetSelectedAreasSummary();
                DrawSelectedText(m_AreaSelectionSummary);

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawFilters()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));

            m_Preferences.filters = BoldFoldout(m_Preferences.filters, Styles.FiltersFoldout);
            if (m_Preferences.filters)
            {
                EditorGUI.indentLevel++;

                DrawAssemblyFilter();
                DrawAreaFilter();

                EditorGUI.BeginChangeCheck();

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(Styles.TextSearchLabel, GUILayout.Width(80));

                m_TextFilter.searchText = EditorGUILayout.DelayedTextField(m_TextFilter.searchText, GUILayout.Width(180));
                m_ActiveIssueTable.searchString = m_TextFilter.searchText;

                m_TextFilter.matchCase = EditorGUILayout.ToggleLeft(Styles.TextSearchCaseSensitive, m_TextFilter.matchCase, GUILayout.Width(160));

                if (m_DeveloperMode)
                {
                    // this is only available in developer mode because it is still too slow at the moment
                    GUI.enabled = m_ActiveAnalysisView.desc.showDependencyView;
                    m_TextFilter.searchDependencies = EditorGUILayout.ToggleLeft("Call Tree (slow)",
                        m_TextFilter.searchDependencies, GUILayout.Width(160));
                    GUI.enabled = true;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Show :", GUILayout.ExpandWidth(true), GUILayout.Width(80));
                GUI.enabled = m_ActiveAnalysisView.desc.showCritical;

                bool wasShowingCritical = m_Preferences.onlyCriticalIssues;
                m_Preferences.onlyCriticalIssues = EditorGUILayout.ToggleLeft("Only Critical Issues",
                    m_Preferences.onlyCriticalIssues, GUILayout.Width(160));
                GUI.enabled = true;

                if (wasShowingCritical != m_Preferences.onlyCriticalIssues)
                {
                    var analytic = ProjectAuditorAnalytics.BeginAnalytic();
                    var payload = new Dictionary<string, string>();
                    payload["selected"] = m_ActiveAnalysisView.desc.showCritical ? "true" : "false";
                    ProjectAuditorAnalytics.SendUIButtonEvent(ProjectAuditorAnalytics.UIButton.OnlyCriticalIssues,
                        analytic);
                }

                bool wasDisplayingMuted = m_Preferences.mutedIssues;
                m_Preferences.mutedIssues = EditorGUILayout.ToggleLeft("Muted Issues",
                    m_Preferences.mutedIssues, GUILayout.Width(127));

                if (wasDisplayingMuted != m_Preferences.mutedIssues)
                {
                    var analytic = ProjectAuditorAnalytics.BeginAnalytic();
                    var payload = new Dictionary<string, string>();
                    payload["selected"] = m_Preferences.mutedIssues ? "true" : "false";
                    ProjectAuditorAnalytics.SendUIButtonEventWithKeyValues(ProjectAuditorAnalytics.UIButton.ShowMuted,
                        analytic, payload);
                }

                EditorGUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck()) m_ShouldRefresh = true;

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawActions()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));

            m_Preferences.actions = BoldFoldout(m_Preferences.actions, Styles.ActionsFoldout);
            if (m_Preferences.actions)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Selected :", GUILayout.ExpandWidth(true), GUILayout.Width(80));

                if (GUILayout.Button(Styles.MuteButton, GUILayout.ExpandWidth(true), GUILayout.Width(100)))
                {
                    var analytic = ProjectAuditorAnalytics.BeginAnalytic();
                    var selectedItems = m_ActiveIssueTable.GetSelectedItems();
                    foreach (var item in selectedItems)
                    {
                        SetRuleForItem(item, Rule.Action.None);
                    }

                    if (!m_Preferences.mutedIssues)
                    {
                        m_ActiveIssueTable.SetSelection(new List<int>());
                    }

                    ProjectAuditorAnalytics.SendUIButtonEventWithSelectionSummary(ProjectAuditorAnalytics.UIButton.Mute,
                        analytic, m_ActiveIssueTable.GetSelectedItems());
                }

                if (GUILayout.Button(Styles.UnmuteButton, GUILayout.ExpandWidth(true), GUILayout.Width(100)))
                {
                    var analytic = ProjectAuditorAnalytics.BeginAnalytic();
                    var selectedItems = m_ActiveIssueTable.GetSelectedItems();
                    foreach (var item in selectedItems)
                    {
                        ClearRulesForItem(item);
                    }

                    ProjectAuditorAnalytics.SendUIButtonEventWithSelectionSummary(
                        ProjectAuditorAnalytics.UIButton.Unmute, analytic, m_ActiveIssueTable.GetSelectedItems());
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        public void SetAssemblySelection(TreeViewSelection selection)
        {
            m_AssemblySelection = selection;
            RefreshDisplay();
        }

        void UpdateAssemblySelection()
        {
            if (m_AssemblyNames == null)
                return;

            if (m_AssemblySelection == null) m_AssemblySelection = new TreeViewSelection();

            m_AssemblySelection.selection.Clear();
            if (!string.IsNullOrEmpty(m_AssemblySelectionSummary))
            {
                if (m_AssemblySelectionSummary == "All")
                {
                    m_AssemblySelection.SetAll(m_AssemblyNames);
                }
                else if (m_AssemblySelectionSummary != "None")
                {
                    var assemblies = m_AssemblySelectionSummary.Split(new[] {", "}, StringSplitOptions.None)
                        .Where(assemblyName => m_AssemblyNames.Contains(assemblyName));
                    if (assemblies.Count() > 0)
                        foreach (var assembly in assemblies)
                            m_AssemblySelection.selection.Add(assembly);
                }
            }

            if (!m_AssemblySelection.selection.Any())
            {
                // initial selection setup:
                // - assemblies from user scripts or editable packages, or
                // - default assembly, or,
                // - all generated assemblies

                var compiledAssemblies = m_AssemblyNames.Where(a => !AssemblyHelper.IsModuleAssembly(a));
                compiledAssemblies = compiledAssemblies.Where(a =>
                    !AssemblyHelper.IsAssemblyReadOnly(a));
                m_AssemblySelection.selection.AddRange(compiledAssemblies);

                if (!m_AssemblySelection.selection.Any())
                {
                    if (m_AssemblyNames.Contains(AssemblyHelper.DefaultAssemblyName))
                        m_AssemblySelection.Set(AssemblyHelper.DefaultAssemblyName);
                    else
                        m_AssemblySelection.SetAll(m_AssemblyNames);
                }
            }

            // update assembly selection summary
            m_AssemblySelectionSummary = GetSelectedAssembliesSummary();
        }

        public void SetAreaSelection(TreeViewSelection selection)
        {
            m_AreaSelection = selection;
            RefreshDisplay();
        }

        void SetRuleForItem(IssueTableItem item, Rule.Action ruleAction)
        {
            var descriptor = item.ProblemDescriptor;

            var callingMethod = "";
            Rule rule;
            if (item.hasChildren)
            {
                rule = m_ProjectAuditor.config.GetRule(descriptor);
            }
            else
            {
                callingMethod = item.ProjectIssue.callingMethod;
                rule = m_ProjectAuditor.config.GetRule(descriptor, callingMethod);
            }

            if (rule == null)
                m_ProjectAuditor.config.AddRule(new Rule
                {
                    id = descriptor.id,
                    filter = callingMethod,
                    action = ruleAction
                });
            else
                rule.action = ruleAction;
        }

        void ClearRulesForItem(IssueTableItem item)
        {
            m_ProjectAuditor.config.ClearRules(item.ProblemDescriptor,
                item.hasChildren ? string.Empty : item.ProjectIssue.callingMethod);
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            {
                GUI.enabled = (m_AnalysisState == AnalysisState.Valid || m_AnalysisState == AnalysisState.NotStarted);

                if (GUILayout.Button(Styles.AnalyzeButton, GUILayout.ExpandWidth(true), GUILayout.Width(80)))
                {
                    Analyze();
                }

                GUI.enabled = m_AnalysisState == AnalysisState.Valid;

                if (ButtonWithDropdownList(Styles.ExportButton, Styles.ExportModeStrings,
                    ExportDropDownCallback, GUILayout.ExpandWidth(true), GUILayout.Width(80)))
                {
                    Export();

                    GUIUtility.ExitGUI();
                }

                GUI.enabled = true;

                if (m_DeveloperMode)
                {
                    if (GUILayout.Button(Styles.ReloadButton, GUILayout.ExpandWidth(true), GUILayout.Width(80)))
                    {
                        Reload();
                    }
                }

                DrawHelpButton();

                if (m_AnalysisState == AnalysisState.InProgress)
                {
                    GUIStyle s = new GUIStyle(EditorStyles.textField);
                    s.normal.textColor = Color.yellow;

                    GUILayout.Label(Styles.AnalysisInProgressLabel, s, GUILayout.ExpandWidth(true));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawMode()
        {
            EditorGUILayout.BeginHorizontal();

            var activeModeIndex = GUILayout.Toolbar(m_ActiveModeIndex, m_ModeNames,
                GUILayout.MaxWidth(LayoutSize.ModeTabWidth), GUILayout.Height(LayoutSize.ToolbarHeight));

            EditorGUILayout.EndHorizontal();

            bool activeModeChanged = (m_ActiveModeIndex != activeModeIndex);
            if (activeModeChanged)
            {
                var analytic = ProjectAuditorAnalytics.BeginAnalytic();
                m_ActiveModeIndex = activeModeIndex;

                RefreshDisplay();

                ProjectAuditorAnalytics.SendUIButtonEvent(m_ActiveAnalysisView.desc.analyticsEvent, analytic);
            }
        }

        void DrawHelpbox()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);

            var helpStyle = new GUIStyle(EditorStyles.textField);
            helpStyle.wordWrap = true;

            EditorGUILayout.LabelField(Styles.HelpText, helpStyle);

            EditorGUILayout.EndVertical();
        }

        void DrawHelpButton()
        {
            if (GUILayout.Button(Styles.HelpButton, EditorStyles.toolbarButton, GUILayout.MaxWidth(25)))
            {
                Application.OpenURL("https://github.com/mtrive/ProjectAuditor/blob/master/Documentation~/index.md");
            }
        }

        void DrawSettings()
        {
            if (m_DeveloperMode)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Build :", GUILayout.ExpandWidth(true), GUILayout.Width(80));
                m_ProjectAuditor.config.AnalyzeOnBuild = EditorGUILayout.ToggleLeft("Auto Analyze",
                    m_ProjectAuditor.config.AnalyzeOnBuild, GUILayout.Width(100));
                m_ProjectAuditor.config.FailBuildOnIssues = EditorGUILayout.ToggleLeft("Fail on Issues",
                    m_ProjectAuditor.config.FailBuildOnIssues, GUILayout.Width(100));
                m_Preferences.emptyGroups = EditorGUILayout.ToggleLeft("Show Empty Groups",
                    m_Preferences.emptyGroups, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
            }
        }

        static void OpenDescriptor(ProblemDescriptor descriptor)
        {
            if (descriptor.type.StartsWith("UnityEngine."))
            {
                var type = descriptor.type.Substring("UnityEngine.".Length);
                var method = descriptor.method;

                Application.OpenURL(string.Format("https://docs.unity3d.com/ScriptReference/{0}{1}{2}.html", type, Char.IsUpper(method[0]) ? "." : "-", method));
            }
        }

        static void OpenTextFile(Location location)
        {
            var obj = AssetDatabase.LoadAssetAtPath<TextAsset>(location.Path);
            if (obj != null)
            {
                // open text file in the text editor
                AssetDatabase.OpenAsset(obj, location.Line);
            }
        }

        static void OpenProjectSettings(Location location)
        {
#if UNITY_2018_3_OR_NEWER
            var window = SettingsService.OpenProjectSettings(location.Path);
            window.Repaint();
#endif
        }

        static void FocusOnAssetInProjectWindow(Location location)
        {
            // Note that LoadMainAssetAtPath might fails, for example if there is a compile error in the script associated with the asset.
            //
            // Instead, we should use GetMainAssetInstanceID and FrameObjectInProjectWindow internal methods:
            //    var instanceId = AssetDatabase.GetMainAssetInstanceID(location.Path);
            //    ProjectWindowUtil.FrameObjectInProjectWindow(instanceId);

            var obj = AssetDatabase.LoadMainAssetAtPath(location.Path);
            if (obj != null)
            {
                ProjectWindowUtil.ShowCreatedAsset(obj);
            }
        }

#if UNITY_2018_1_OR_NEWER
        [MenuItem("Window/Analysis/Project Auditor")]
#else
        [MenuItem("Window/Project Auditor")]
#endif
        public static ProjectAuditorWindow ShowWindow()
        {
            var wnd = GetWindow(typeof(ProjectAuditorWindow)) as ProjectAuditorWindow;
            if (wnd != null) wnd.titleContent = Styles.WindowTitle;
            return wnd;
        }

        // UI styles and layout
        static class LayoutSize
        {
            public static readonly int ToolbarWidth = 600;
            public static readonly int ToolbarHeight = 30;
            public static readonly int FoldoutWidth = 300;
            public static readonly int FoldoutMinHeight = 100;
            public static readonly int FoldoutMaxHeight = 220;
            public static readonly int FilterOptionsLeftLabelWidth = 100;
            public static readonly int FilterOptionsEnumWidth = 50;
            public static readonly int ModeTabWidth = 300;
            public static readonly int DependencyViewHeight = 200;
        }

        static class Styles
        {
            public static readonly GUIContent DeveloperMode = new GUIContent("Developer Mode");
            public static readonly GUIContent UserMode = new GUIContent("User Mode");

            public static readonly GUIContent WindowTitle = new GUIContent("Project Auditor");

            public static readonly GUIContent AnalyzeButton =
                new GUIContent("Analyze", "Analyze Project and list all issues found.");

            public static readonly GUIContent AnalysisInProgressLabel =
                new GUIContent("Analysis in progress...", "Analysis in progress...please wait.");

            public static readonly GUIContent ReloadButton =
                new GUIContent("Reload DB", "Reload Issue Definition files.");

            public static readonly GUIContent ExportButton =
                new GUIContent("Export", "Export project report to .csv files.");

            public static readonly GUIContent AssemblyFilter =
                new GUIContent("Assembly : ", "Select assemblies to examine");

            public static readonly GUIContent AssemblyFilterSelect =
                new GUIContent("Select", "Select assemblies to examine");

            public static readonly GUIContent AreaFilter =
                new GUIContent("Area : ", "Select performance areas to display");

            public static readonly GUIContent AreaFilterSelect =
                new GUIContent("Select", "Select performance areas to display");

            public static readonly GUIContent TextSearchLabel =
                new GUIContent("Search : ", "Text search options");

            public static readonly GUIContent TextSearchCaseSensitive =
                new GUIContent("Match Case", "Case-sensitive search");

            public static readonly GUIContent MuteButton = new GUIContent("Mute", "Always ignore selected issues.");
            public static readonly GUIContent UnmuteButton = new GUIContent("Unmute", "Always show selected issues.");

            public static readonly GUIContent FiltersFoldout = new GUIContent("Filters", "Filtering Criteria");
            public static readonly GUIContent ActionsFoldout = new GUIContent("Actions", "Actions on selected issues");
            public static readonly GUIContent DetailsFoldout = new GUIContent("Details", "Issue Details");

            public static readonly GUIContent RecommendationFoldout =
                new GUIContent("Recommendation", "Recommendation on how to solve the issue");

#if UNITY_2018_1_OR_NEWER
            public static readonly GUIContent HelpButton = EditorGUIUtility.TrIconContent("_Help", "Open Manual (in a web browser)");
#else
            public static readonly GUIContent HelpButton = new GUIContent("?", "Open Manual (in a web browser)");
#endif

            public static readonly string[] ExportModeStrings =
            {
                "All",
                "Filtered",
                "Selected"
            };

            public static readonly string HelpText =
@"Project Auditor is an experimental static analysis tool for Unity Projects.
This tool will analyze scripts and project settings of any Unity project
and report a list a possible problems that might affect performance, memory and other areas.

To Analyze the project:
* Click on Analyze.

Once the project is analyzed, the tool displays list of issues.
At the moment there are two types of issues: API calls or Project Settings. The tool allows the user to switch between the two.
In addition, it is possible to filter issues by area (CPU/Memory/etc...) or assembly name or search for a specific string.";
        }
    }
}
