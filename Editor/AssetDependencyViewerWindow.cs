using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using static AssetDependencyViewer.AssetDependencySummary;

namespace AssetDependencyViewer
{
    public sealed class AssetDependencyViewerWindow : EditorWindow
    {
        private string SerializePath => Application.persistentDataPath + "/dep.dat";

        private AssetInfo m_SelectionAssetInfo;
        private readonly List<AssetInfo> m_SelectionAssetInfoHistory = new();
        private int m_SelectionAssetInfoHistoryIndex;
        private readonly AssetDependencySummary m_Summary = new();
        private bool m_UpdateWithPackages = true;
        private bool m_UpdateWithFolder = true;
        private Vector2 m_ScrollPositionUses;
        private Vector2 m_ScrollPositionUsed;

        private IMGUIContainer m_UsesPane;
        private IMGUIContainer m_UsedPane;
        private GUIStyle m_SelectionLabelStyle;
        private GUIStyle m_AssetDirectoryLabelStyle;
        private GUIStyle m_AssetFileNameLabelStyle;
        private GUIStyle m_LastUpdatedLabelStyle;
        private GUIStyle m_ColumnStyle;
        private GUIContent m_GuiContentForward;
        private GUIContent m_GuiContentBack;

        [MenuItem("Window/Asset Dependency Viewer")]
        public static void Open()
        {
            var window = GetWindow<AssetDependencyViewerWindow>("Asset Dependency Viewer");
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.projectWindowItemOnGUI += ProjectWindowItemOnGUI;

            m_Summary.DeSerialize(SerializePath);
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.projectWindowItemOnGUI -= ProjectWindowItemOnGUI;
        }
        
        private void OnSelectionChanged()
        {
            if (Selection.assetGUIDs.Length == 0 || m_Summary?.AssetInfoMap == null)
            {
                return;
            }
            
            var path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);

            if (m_Summary.AssetInfoMap.TryGetValue(path, out var assetInfo))
            {
                SelectAssetInfo(assetInfo, updateHistory: true);
            }
        }

        private void ProjectWindowItemOnGUI(string guid, Rect rect)
        {
            if (m_Summary?.AssetInfoMap == null)
            {
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!m_Summary.AssetInfoMap.TryGetValue(path, out var assetInfo))
            {
                return;
            }

            var length = assetInfo.UsedByIndex.Length;
            if (length <= 0)
            {
                return;
            }

            var labelContent = new GUIContent(length.ToString());
            var labelSize = GUI.skin.label.CalcSize(labelContent);

            rect.xMin = rect.xMax - labelSize.x;
            rect.width = labelSize.x;
            GUI.Label(rect, labelContent);
        }

        private void SelectAssetInfo(AssetInfo assetInfo, bool updateHistory)
        {
            if (m_SelectionAssetInfo == assetInfo)
            {
                return;
            }

            if (updateHistory)
            {
                var forwardIndex = m_SelectionAssetInfoHistoryIndex + 1;
                if (forwardIndex < m_SelectionAssetInfoHistory.Count)
                {
                    m_SelectionAssetInfoHistory.RemoveRange(
                        forwardIndex,
                        m_SelectionAssetInfoHistory.Count - forwardIndex);
                }

                m_SelectionAssetInfoHistory.Add(assetInfo);
                m_SelectionAssetInfoHistoryIndex = m_SelectionAssetInfoHistory.Count - 1;
            }

            m_SelectionAssetInfo = assetInfo;
            Repaint();
        }

        private void CreateGUI()
        {
            var headerPane = new IMGUIContainer(DrawHeader);
            rootVisualElement.Add(headerPane);
            
            var splitView = new TwoPaneSplitView(0, position.width * 0.5f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.Add(m_UsesPane = new IMGUIContainer(DrawUsesPane));
            splitView.Add(m_UsedPane = new IMGUIContainer(DrawUsedPane));
            rootVisualElement.Add(splitView);
        }
        
        private void DrawHeader()
        {
            InitializeGuiStyles();

            using var _ = new EditorGUILayout.HorizontalScope(EditorStyles.toolbar);

            void HistoryMoveButton(GUIContent gUIContent, int move)
            {
                var index = m_SelectionAssetInfoHistoryIndex + move;
                if (index < 0 || index >= m_SelectionAssetInfoHistory.Count)
                {
                    var color = GUI.contentColor;
                    GUI.contentColor = Color.black;
                    GUILayout.Button(gUIContent, GUILayout.ExpandHeight(true));
                    GUI.contentColor = color;
                    return;
                }

                if (GUILayout.Button(gUIContent, GUILayout.ExpandHeight(true)))
                {
                    m_SelectionAssetInfoHistoryIndex = index;

                    var assetInfo = m_SelectionAssetInfoHistory[index];
                    SelectAssetInfo(assetInfo, updateHistory: false);
                }
            }

            HistoryMoveButton(m_GuiContentBack, -1);
            HistoryMoveButton(m_GuiContentForward, 1);

            if (m_SelectionAssetInfo != null)
            {
                var labelContent = new GUIContent(m_SelectionAssetInfo.FileName);
                var labelSize = m_SelectionLabelStyle.CalcSize(labelContent);

                var rect = EditorGUILayout.GetControlRect(
                    false, GUILayout.Width(labelSize.x), GUILayout.Height(labelSize.y));

                if (IsMouseDownOnRect(rect))
                {
                    PingPath(m_Summary.PathList[m_SelectionAssetInfo.PathIndex]);
                }

                GUI.Label(rect, labelContent, m_SelectionLabelStyle);
            }
                
            GUILayout.FlexibleSpace();

            if (m_Summary.LastUpdatedTime != default)
            {
                GUILayout.Label($"Last updated: {m_Summary.LastUpdatedTime}", m_LastUpdatedLabelStyle);
            }

            GUILayout.Space(4);

            if (GUILayout.Button("Update"))
            {
                m_Summary.Build(m_UpdateWithPackages, m_UpdateWithFolder);
                m_Summary.Serialize(SerializePath);
            }

            GUILayout.Space(8);

            m_UpdateWithPackages = GUILayout.Toggle(m_UpdateWithPackages, "With Packages");
            m_UpdateWithFolder = GUILayout.Toggle(m_UpdateWithFolder, "With Folder");
        }

        private void DrawUsesPane()
        {
            if (m_SelectionAssetInfo?.UsesAssetInfo == null)
            {
                return;
            }
            
            using var areaScope = new GUILayout.AreaScope(m_UsesPane.contentRect);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Uses ({m_SelectionAssetInfo.UsesAssetInfo.Length})");
                GUILayout.FlexibleSpace();
            }

            using var scroll = new GUILayout.ScrollViewScope(m_ScrollPositionUses, EditorStyles.helpBox);
            m_ScrollPositionUses = scroll.scrollPosition;

            DrawColumns(m_SelectionAssetInfo.UsesAssetInfo);
        }
        
        private void DrawUsedPane()
        {
            if (m_SelectionAssetInfo?.UsedByAssetInfo == null)
            {
                return;
            }

            using var areaScope = new GUILayout.AreaScope(m_UsedPane.contentRect);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Used By ({m_SelectionAssetInfo.UsedByIndex.Length})");
                GUILayout.FlexibleSpace();
            }

            using var scroll = new GUILayout.ScrollViewScope(m_ScrollPositionUsed, EditorStyles.helpBox);
            m_ScrollPositionUsed = scroll.scrollPosition;

            DrawColumns(m_SelectionAssetInfo.UsedByAssetInfo);
        }

        private void DrawColumns(AssetInfo[] assetInfos)
        {
            var length = assetInfos.Length;
            for (var index = 0; index < length; ++index)
            {
                DrawColumn(assetInfos[index], index % 2 == 1);
            }
        }

        private void DrawColumn(AssetInfo assetInfo, bool odd)
        {
            var color = GUI.backgroundColor;
            GUI.backgroundColor = odd ? Color.white : new Color(0.7f, 0.7f, 0.7f);

            using var horizontalScope = new EditorGUILayout.HorizontalScope(m_ColumnStyle);

            if (IsMouseDownOnRect(horizontalScope.rect))
            {
                PingPath(m_Summary.PathList[assetInfo.PathIndex]);

                if (Event.current.control)
                {
                    SelectAssetInfo(assetInfo, updateHistory: true);
                }

                Event.current.Use();
            }

            var height = EditorGUIUtility.singleLineHeight;

            if (assetInfo.Icon == null)
            {
                assetInfo.Icon = AssetDatabase.GetCachedIcon(m_Summary.PathList[assetInfo.PathIndex]) as Texture2D;
            }

            GUILayout.Box(assetInfo.Icon, GUIStyle.none, GUILayout.Width(height), GUILayout.Height(height));

            GUILayout.Label(assetInfo.Directory, m_AssetDirectoryLabelStyle, GUILayout.Height(height));

            GUILayout.Label(assetInfo.FileName, m_AssetFileNameLabelStyle, GUILayout.Height(height));

            GUILayout.FlexibleSpace();

            GUI.backgroundColor = color;
        }

        private bool IsMouseDownOnRect(Rect rect)
        {
            var currentEvent = Event.current;
            return
                currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                rect.Contains(currentEvent.mousePosition);
        }

        private void PingPath(string path)
        {
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
        }

        private void InitializeGuiStyles()
        {
            if (m_SelectionLabelStyle != null)
            {
                return;
            }

            var template = GUI.skin.label;

            m_SelectionLabelStyle = new GUIStyle(template)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = new GUIStyleState
                {
                    textColor = new Color(0.7f, 0.7f, 1.0f)
                }
            };

            m_AssetDirectoryLabelStyle = new GUIStyle(template)
            {
                margin = new RectOffset(0, 0, template.margin.top, template.margin.bottom),
                fontSize = 10,
                normal = new GUIStyleState
                {
                    textColor = new Color(0.6f, 0.6f, 0.6f)
                },
                hover = new GUIStyleState
                {
                    textColor = new Color(0.6f, 0.6f, 0.6f)
                }
            };

            m_AssetFileNameLabelStyle = new GUIStyle(template)
            {
                margin = new RectOffset(0, template.margin.right, template.margin.top, template.margin.bottom),
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = new GUIStyleState
                {
                    textColor = new Color(0.85f, 0.85f, 0.85f)
                },
                hover = new GUIStyleState
                {
                    textColor = new Color(0.85f, 0.85f, 0.85f)
                }
            };

            m_LastUpdatedLabelStyle = new GUIStyle(template)
            {
                fontSize = 12,
                normal = new GUIStyleState
                {
                    textColor = Color.gray
                }
            };

            m_ColumnStyle = new GUIStyle(GUI.skin.box)
            {
                margin = new RectOffset(4, 4, 2, 2),
            };

            m_GuiContentForward = new GUIContent(EditorGUIUtility.IconContent("forward"));
            m_GuiContentBack = new GUIContent(EditorGUIUtility.IconContent("back"));
        }
    }
}