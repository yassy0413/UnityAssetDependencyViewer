using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetDependencyViewer
{
    public sealed class AssetDependencySummary
    {
        public string[] PathList;
        public Dictionary<string, AssetInfo> AssetInfoMap;
        public string LastUpdatedTime;
        private AssetInfo[] m_AssetInfoList;

        private const string DisplayProgressTitle = "Updating asset dependencies.";

        public sealed class AssetInfo
        {
            public int PathIndex;
            public int[] UsesIndex;
            public int[] UsedByIndex;

            public string Directory;
            public string FileName;
            public AssetInfo[] UsesAssetInfo;
            public AssetInfo[] UsedByAssetInfo;
            public Texture2D Icon;

            public void WriteTo(BinaryWriter bs)
            {
                bs.Write(PathIndex);

                bs.Write(UsesIndex.Length);
                foreach (var index in UsesIndex)
                {
                    bs.Write(index);
                }

                bs.Write(UsedByIndex.Length);
                foreach (var index in UsedByIndex)
                {
                    bs.Write(index);
                }
            }

            public void ReadFrom(BinaryReader bs)
            {
                PathIndex = bs.ReadInt32();

                UsesIndex = new int[bs.ReadInt32()];
                for (var index = 0; index < UsesIndex.Length; ++index)
                {
                    UsesIndex[index] = bs.ReadInt32();
                }

                UsedByIndex = new int[bs.ReadInt32()];
                for (var index = 0; index < UsedByIndex.Length; ++index)
                {
                    UsedByIndex[index] = bs.ReadInt32();
                }
            }
        }

        public void Serialize(string path)
        {
            using var ms = new MemoryStream(1024 * 1024 * 16);
            using var bs = new BinaryWriter(ms);

            bs.Write(m_AssetInfoList.Length);
            foreach (var assetInfo in m_AssetInfoList)
            {
                assetInfo.WriteTo(bs);
            }

            bs.Write(PathList.Length);
            foreach (var v in PathList)
            {
                bs.Write(v);
            }

            File.WriteAllBytes(path, ms.ToArray());

            LastUpdatedTime = $"{File.GetLastWriteTime(path):g}";
        }

        public void DeSerialize(string path)
        {
            if (AssetInfoMap != null || !File.Exists(path))
            {
                return;
            }

            LastUpdatedTime = $"{File.GetLastWriteTime(path):g}";

            var bytes = File.ReadAllBytes(path);

            using var ms = new MemoryStream(bytes);
            using var bs = new BinaryReader(ms);

            m_AssetInfoList = new AssetInfo[bs.ReadInt32()];
            for (var index = 0; index < m_AssetInfoList.Length; ++index)
            {
                m_AssetInfoList[index] = new AssetInfo();
                m_AssetInfoList[index].ReadFrom(bs);
            }

            PathList = new string[bs.ReadInt32()];
            for (var index = 0; index < PathList.Length; ++index)
            {
                PathList[index] = bs.ReadString();
            }

            Setup();
        }

        public void Build(bool withPackages, bool withFolder)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var searchInFolders = new List<string> { "Assets" };
            if (withPackages)
            {
                searchInFolders.Add("Packages");
            }

            var pathList = AssetDatabase
                .FindAssets("", searchInFolders.ToArray())
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(x => withFolder || !AssetDatabase.IsValidFolder(x))
                .ToList();

            var pathListCount = pathList.Count;

            var pathIndexMap = Enumerable.Range(0, pathListCount)
                .ToDictionary(x => pathList[x], x => x);

            int PathToIndex(string path)
            {
                if (!pathIndexMap.TryGetValue(path, out var index))
                {
                    index = pathList.Count();
                    pathList.Add(path);
                    pathIndexMap.Add(path, index);
                }
                return index;
            }

            var assetInfoList = Enumerable.Range(0, pathListCount)
                .Select(x =>
                {
                    var path = pathList[x];

                    EditorUtility.DisplayProgressBar(
                        DisplayProgressTitle, path, (x + 1f) / pathListCount);

                    return new AssetInfo
                    {
                        PathIndex = x,
                        UsesIndex = AssetDatabase.GetDependencies(path, false)
                            .Where(y => withFolder || !AssetDatabase.IsValidFolder(y))
                            .Select(PathToIndex)
                            .ToArray()
                    };
                })
                .ToList();

            EditorUtility.DisplayProgressBar(DisplayProgressTitle, "", 1f);

            PathList = pathList.ToArray();
            pathListCount = pathList.Count();

            for (var index = assetInfoList.Count(); index < pathListCount; ++index)
            {
                assetInfoList.Add(new AssetInfo
                {
                    PathIndex = index,
                    UsesIndex = Array.Empty<int>()
                });
            }

            m_AssetInfoList = assetInfoList.ToArray();

            EditorUtility.DisplayProgressBar(DisplayProgressTitle, "Find used index", 1f);

            var usedIndexList = new List<int>[pathListCount];

            for (var index = 0; index < pathListCount; ++index)
            {
                usedIndexList[index] = new List<int>();
            }

            foreach (var assetInfo in m_AssetInfoList)
            {
                foreach (var index in assetInfo.UsesIndex)
                {
                    usedIndexList[index].Add(assetInfo.PathIndex);
                }
            }

            Parallel.ForEach(m_AssetInfoList, x =>
            {
                x.UsedByIndex = usedIndexList[x.PathIndex]
                    .OrderBy(x => PathList[x])
                    .ToArray();
            });

            EditorUtility.DisplayProgressBar(DisplayProgressTitle, "Setup", 1f);

            Setup();

            EditorUtility.ClearProgressBar();
            Debug.Log($"AssetDependencyViewer updated. {sw.Elapsed.TotalSeconds}sec {pathListCount}assets.");
        }

        private void Setup()
        {
            AssetInfoMap = m_AssetInfoList.ToDictionary(x => PathList[x.PathIndex]);

            Parallel.ForEach(m_AssetInfoList, x =>
            {
                x.Directory = Path.GetDirectoryName(PathList[x.PathIndex]);
                x.FileName = Path.GetFileName(PathList[x.PathIndex]);
                x.UsesAssetInfo = GetAssetInfoList(x.UsesIndex);
                x.UsedByAssetInfo = GetAssetInfoList(x.UsedByIndex);
            });
        }

        private AssetInfo[] GetAssetInfoList(int[] pathList)
        {
            return pathList
                .Select(x => AssetInfoMap[PathList[x]])
                .ToArray();
        }
    }
}
