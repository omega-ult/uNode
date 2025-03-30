#if UNITY_EDITOR
namespace MaxyGames.OdinSerializer.Utilities.Editor
{
    using OdinSerializer.Editor;
    using UnityEditor;
    using UnityEditor.Build;
    using System.IO;
    using System;
    using System.Collections.Generic;
    using OdinSerializer.Utilities;
    using System.Reflection;
#if UNITY_2018_1_OR_NEWER
    using UnityEditor.Build.Reporting;
#endif

    public static class OdinBuildAutomation
    {
        private static readonly string EditorAssemblyPath;
        private static readonly string JITAssemblyPath;
        private static readonly string AOTAssemblyPath;
        private static readonly string GenerateAssembliesDir;

		public static string ss;

        static OdinBuildAutomation()
        {
            // 使用PackageInfo API获取包的正确路径
            var assembly = typeof(AssemblyImportSettingsUtilities).Assembly;
            string assemblyPath = new Uri(assembly.CodeBase).LocalPath;
            string packageRelativePath = "";
            
            // 尝试从程序集路径中提取包信息
            if (assemblyPath.Contains("Packages"))
            {
                int packagesIndex = assemblyPath.IndexOf("Packages");
                string pathAfterPackages = assemblyPath.Substring(packagesIndex + "Packages".Length + 1);
                
                // 提取包名部分
                int firstSlashIndex = pathAfterPackages.IndexOf('/');
                if (firstSlashIndex > 0)
                {
                    string packageName = pathAfterPackages.Substring(0, firstSlashIndex);
                    
                    // 使用Unity包API获取完整路径
                    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{packageName}");
                    if (packageInfo != null)
                    {
                        // 使用包的正确路径
                        packageRelativePath = $"Packages/{packageInfo.name}";
                    }
                }
            }
            
            // 如果无法通过包API获取，则回退到原始方法
            if (string.IsNullOrEmpty(packageRelativePath))
            {
                var odinSerializerDir = new DirectoryInfo(assembly.GetAssemblyDirectory())
                    .Parent.FullName.Replace('\\', '/').Replace("//", "/").TrimEnd('/');

                var unityDataPath = Environment.CurrentDirectory.Replace("\\", "//").Replace("//", "/").TrimEnd('/');

                if (!odinSerializerDir.StartsWith(unityDataPath))
                {
                    throw new FileNotFoundException("The referenced Odin Serializer assemblies are not inside the current Unity project - cannot use build automation script!");
                }

                odinSerializerDir = odinSerializerDir.Substring(unityDataPath.Length).TrimStart('/');
                packageRelativePath = odinSerializerDir;
            }
            
            // 使用获取到的包路径构建最终路径
            EditorAssemblyPath    = packageRelativePath + "/EditorOnly/MaxyGames.OdinSerializer.dll";
            AOTAssemblyPath       = packageRelativePath + "/AOT/MaxyGames.OdinSerializer.dll";
            JITAssemblyPath       = packageRelativePath + "/JIT/MaxyGames.OdinSerializer.dll";
            GenerateAssembliesDir = packageRelativePath + "/Generated";
            
            if  (!File.Exists(EditorAssemblyPath))  throw new FileNotFoundException("Make sure all release configurations specified in the Visual Studio project are built.", EditorAssemblyPath);
            else if (!File.Exists(AOTAssemblyPath)) throw new FileNotFoundException("Make sure all release configurations specified in the Visual Studio project are built.", AOTAssemblyPath);
            else if (!File.Exists(JITAssemblyPath)) throw new FileNotFoundException("Make sure all release configurations specified in the Visual Studio project are built.", JITAssemblyPath);
        }

        private static string GetAssemblyDirectory(this Assembly assembly)
        {
            string filePath = new Uri(assembly.CodeBase).LocalPath;
            return Path.GetDirectoryName(filePath);
        }

        public static void OnPreprocessBuild()
        {
            BuildTarget platform = EditorUserBuildSettings.activeBuildTarget;

			try
            {
                // The EditorOnly dll should aways have the same import settings. But lets just make sure.
                AssemblyImportSettingsUtilities.SetAssemblyImportSettings(platform, EditorAssemblyPath, OdinAssemblyImportSettings.IncludeInEditorOnly);

				if (AssemblyImportSettingsUtilities.IsJITSupported(
                    platform,
                    AssemblyImportSettingsUtilities.GetCurrentScriptingBackend(),
                    AssemblyImportSettingsUtilities.GetCurrentApiCompatibilityLevel()))
                {
                    AssemblyImportSettingsUtilities.SetAssemblyImportSettings(platform, AOTAssemblyPath, OdinAssemblyImportSettings.ExcludeFromAll);
                    AssemblyImportSettingsUtilities.SetAssemblyImportSettings(platform, JITAssemblyPath, OdinAssemblyImportSettings.IncludeInBuildOnly);
                }
                else
                {
                    AssemblyImportSettingsUtilities.SetAssemblyImportSettings(platform, AOTAssemblyPath, OdinAssemblyImportSettings.IncludeInBuildOnly);
                    AssemblyImportSettingsUtilities.SetAssemblyImportSettings(platform, JITAssemblyPath, OdinAssemblyImportSettings.ExcludeFromAll);

					// Generates dll that contains all serialized generic type variants needed at runtime.
					if (UNode.Editors.uNodeEditorInitializer.AOTScan(out var types)) {
                        AOTSupportUtilities.GenerateDLL(GenerateAssembliesDir, "OdinAOTSupport", types);
                    }
                }
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
        
        public static void OnPostprocessBuild()
        {
            // Delete Generated AOT support dll after build so it doesn't pollute the project.
            if (Directory.Exists(GenerateAssembliesDir))
            {
                Directory.Delete(GenerateAssembliesDir, true);
                File.Delete(GenerateAssembliesDir + ".meta");
                AssetDatabase.Refresh();
            }
        }
    }

#if UNITY_2018_1_OR_NEWER
    public class OdinPreBuildAutomation : IPreprocessBuildWithReport
#else
    public class OdinPreBuildAutomation : IPreprocessBuild
#endif
    {
        public int callbackOrder { get { return -1000; } }

#if UNITY_2018_1_OR_NEWER
	    public void OnPreprocessBuild(BuildReport report)
	    {
            OdinBuildAutomation.OnPreprocessBuild();
	    }
#else
        public void OnPreprocessBuild(BuildTarget target, string path)
        {
            OdinBuildAutomation.OnPreprocessBuild();
        }
#endif
    }

#if UNITY_2018_1_OR_NEWER
    public class OdinPostBuildAutomation : IPostprocessBuildWithReport
#else
    public class OdinPostBuildAutomation : IPostprocessBuild
#endif
    {
        public int callbackOrder { get { return -1000; } }

#if UNITY_2018_1_OR_NEWER
	    public void OnPostprocessBuild(BuildReport report)
	    {
            OdinBuildAutomation.OnPostprocessBuild();
	    }
#else
        public void OnPostprocessBuild(BuildTarget target, string path)
        {
            OdinBuildAutomation.OnPostprocessBuild();

        }
#endif
    }
}
#endif