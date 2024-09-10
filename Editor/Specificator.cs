using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if LIL_VRCSDK_AVATARS
using VRC.SDKBase;
using VRC.SDKBase.Editor.Validation;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace jp.lilxyzw.specificator
{
    internal class Specificator : EditorWindow
    {
        public Vector2 scrollPos;
        public string productFolder = "";
        public DefaultAsset targetFolder;
        public Object target;
        public string licenseText = "";
        public string trialText = "";
        public string creditText = "";
        public string prefix = "# ";
        public string postfix = "";
        public string liststyle = "- ";
        public bool generateBlendShapeList = false;
        public string texOther = "";
        public bool modelMB = false;
        public string modelOther = "";
        public bool sceneMissmatch = false;
        public TextAsset templateJson;
        public Template template = new();

        private static readonly string versionFull = Application.unityVersion;
        private static readonly string versionMini = versionFull.Substring(0, versionFull.IndexOf('.', versionFull.IndexOf('.')+1));

        [NonSerialized] private string generatedText = "";
        [NonSerialized] private bool isScanned = false;
        [NonSerialized] private readonly HashSet<(string,string)> packages = new();
        [NonSerialized] private readonly HashSet<string> scriptPaths = new();
        [NonSerialized] private readonly Dictionary<string, IEnumerable<string>> blendshapes = new();
        [NonSerialized] private int polyCount = 0;
        [NonSerialized] private int materialCount = 0;
        [NonSerialized] private int textureCount = 0;
        [NonSerialized] private int animCount = 0;
        [NonSerialized] private int blendShapeCount = 0;
        [NonSerialized] private int productMaterial = 0;
        [NonSerialized] private int productTexture = 0;
        [NonSerialized] private int productAnim = 0;
        [NonSerialized] private int productMesh = 0;
        [NonSerialized] private bool texPNG = false;
        [NonSerialized] private bool texPSD = false;
        [NonSerialized] private bool texClip = false;
        [NonSerialized] private bool modelFBX = false;
        [NonSerialized] private bool modelBlend = false;
        #if LIL_VRCSDK_AVATARS
        public bool showAllStats = false;
        [NonSerialized] private AvatarPerformanceStats stats;
        [NonSerialized] private int paramsCount = 0;
        [NonSerialized] private bool isAvatar = false;
        #endif

        void ResetParams()
        {
            texPNG = false;
            texPSD = false;
            texClip = false;
            modelFBX = false;
            modelBlend = false;
            modelMB = false;

            isScanned = false;
            packages.Clear();
            scriptPaths.Clear();
            blendshapes.Clear();
            polyCount = 0;
            materialCount = 0;
            textureCount = 0;
            animCount = 0;
            blendShapeCount = 0;

            productMaterial = 0;
            productTexture = 0;
            productMesh = 0;
            #if LIL_VRCSDK_AVATARS
            stats = null;
            paramsCount = 0;
            isAvatar = false;
            #endif
        }

        [MenuItem("Tools/lilAssetSpecificator")]
        static void Init() => GetWindow<Specificator>("lilAssetSpecificator").Show();

        void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            EditorGUI.BeginChangeCheck();
            
            target = EditorGUILayout.ObjectField("Prefab / シーン（必須）", target, typeof(Object), true);
            targetFolder = EditorGUILayout.ObjectField("商品フォルダ（必須）", targetFolder, typeof(DefaultAsset), false) as DefaultAsset;
            productFolder = EditorGUILayout.TextField("PC上の商品フォルダパス", productFolder);
            if(EditorGUI.EndChangeCheck())
            {
                generatedText = "";
                ResetParams();

                if(targetFolder)
                {
                    var path = AssetDatabase.GetAssetPath(targetFolder);
                    if(string.IsNullOrEmpty(path) || !Directory.Exists(path)) targetFolder = null;
                }
                sceneMissmatch = target is SceneAsset && AssetDatabase.GetAssetPath(target) != SceneManager.GetActiveScene().path;
            }

            if(sceneMissmatch) EditorGUILayout.HelpBox("シーンをスキャンする場合はスキャン前にシーンを開いてください。", MessageType.Error);
            if(!string.IsNullOrEmpty(productFolder) && !Directory.Exists(productFolder)) EditorGUILayout.HelpBox("商品のフォルダ（PC上）が存在しません。", MessageType.Error);

            if(target && targetFolder && !isScanned && !sceneMissmatch) Scan(target);

            // テンプレート読み込み
            EditorGUI.BeginChangeCheck();
            templateJson = EditorGUILayout.ObjectField("テンプレートのJSON", templateJson, typeof(TextAsset), false) as TextAsset;
            if(EditorGUI.EndChangeCheck())
            {
                generatedText = "";
                if(templateJson) template = Template.FromTextAsset(templateJson);
                else template = new Template();
            }

            GUI.enabled = target && targetFolder;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("利用規約（必ず購入前に確認できるようにしてください）", EditorStyles.boldLabel);
            licenseText = EditorGUILayout.TextArea(licenseText);
            if(string.IsNullOrEmpty(licenseText))
            {
                EditorGUILayout.HelpBox("利用規約が入力されていません。", MessageType.Error);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("記入例");
                EditorGUILayout.TextArea("利用規約 (ja): https://drive.google.com/file/d/...\r\nTerms of Use (en): https://drive.google.com/file/d/...\r\nTerms of Use (ko): https://drive.google.com/file/d/...\r\nTerms of Use (zh-Hans): https://drive.google.com/file/d/...");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("試用アバター・試用ワールドの情報（省略可）", EditorStyles.boldLabel);
            trialText = EditorGUILayout.TextArea(trialText);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("クレジット（省略可、サンプル画像のアバターや衣装などを明記することを推奨）", EditorStyles.boldLabel);
            creditText = EditorGUILayout.TextArea(creditText);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("表示項目", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if(blendShapeCount > 0) generateBlendShapeList = EditorGUILayout.ToggleLeft("BlendShapeのリストを生成", generateBlendShapeList);
            #if LIL_VRCSDK_AVATARS
            if(stats != null) showAllStats = EditorGUILayout.ToggleLeft("パフォーマンスランクの情報を全て生成", showAllStats);
            #endif
            EditorGUILayout.EndVertical();

            if(productMesh > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("改変用モデル", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.enabled = false;
                EditorGUILayout.Toggle("FBXファイル（.fbx）", modelFBX);
                EditorGUILayout.Toggle("Blender（.blend）", modelBlend);
                EditorGUILayout.Toggle("Maya（.mb）", modelMB);
                GUI.enabled = target && targetFolder;
                modelOther = EditorGUILayout.TextField("その他", modelOther);
                EditorGUILayout.EndVertical();
            }

            if(productTexture > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("改変用テクスチャ", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.enabled = false;
                EditorGUILayout.Toggle("PNGファイル（.png）", texPNG);
                EditorGUILayout.Toggle("Photoshop（.psd）", texPSD);
                EditorGUILayout.Toggle("CLIP STUDIO（.clip）", texClip);
                GUI.enabled = target && targetFolder;
                texOther = EditorGUILayout.TextField("その他", texOther);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("スタイル", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            prefix = EditorGUILayout.TextField("見出しの前置", prefix);
            postfix = EditorGUILayout.TextField("見出しの後置", postfix);
            EditorGUILayout.LabelField("見出しの表示例: ", $"{prefix}基本仕様{postfix}");
            liststyle = EditorGUILayout.TextField("リストのスタイル", liststyle);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("=== 生成結果（右クリックでCopyできます） ===", EditorStyles.boldLabel);
            if(EditorGUI.EndChangeCheck() || string.IsNullOrEmpty(generatedText) && target && targetFolder) generatedText = BuildText();
            if(scriptPaths.Any()) EditorGUILayout.HelpBox("パッケージ情報（package.json）がなく、名前を特定できなかった前提アセットがあります。アセットが入っているフォルダがそのまま出力されているので適宜修正してください。", MessageType.Warning);
            GUI.enabled = false;
            EditorGUILayout.TextArea(generatedText);

            GUI.enabled = true;
            EditorGUILayout.EndScrollView();
        }

        private void Scan(Object obj)
        {
            isScanned = true;
            if(!obj && !targetFolder) return;

            var scaned = new HashSet<Object>();

            string rootpath = AssetDatabase.GetAssetPath(targetFolder);
            var guids = AssetDatabase.FindAssets("", new[]{rootpath});
            var types = guids.Select(g => AssetDatabase.GetMainAssetTypeFromGUID(new GUID(g)));
            var imptypes = guids.Select(g => AssetDatabase.GetImporterType(new GUID(g)));
            productMaterial = types.Count(t => typeof(Material).IsAssignableFrom(t));
            productTexture = types.Count(t => typeof(Texture).IsAssignableFrom(t));
            productAnim = types.Count(t => typeof(AnimationClip).IsAssignableFrom(t));
            productMesh = imptypes.Count(t => typeof(ModelImporter).IsAssignableFrom(t));

            bool hasRoot = !string.IsNullOrEmpty(rootpath);

            if(obj is GameObject gameObject)
            {
                GetReferenceFromObject(scaned, gameObject.GetComponentsInChildren<Component>(true));
                polyCount += gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => !IsEditorOnly(s)).Select(r => r.sharedMesh).Where(m => m).Sum(m => m.triangles.Length / 3);
                polyCount += gameObject.GetComponentsInChildren<MeshFilter>(true).Where(s => !IsEditorOnly(s)).Select(r => r.sharedMesh).Where(m => m).Sum(m => m.triangles.Length / 3);

                #if LIL_VRCSDK_AVATARS
                isAvatar = gameObject.GetComponent<VRC_AvatarDescriptor>();
                paramsCount = scaned.Where(o => o is VRCExpressionParameters).Sum(o => (o as VRCExpressionParameters).CalcTotalCost());

                bool isMobilePlatform = ValidationEditorHelpers.IsMobilePlatform();
                stats = new AvatarPerformanceStats(isMobilePlatform);
                AvatarPerformance.CalculatePerformanceStats(obj.name, gameObject, stats, isMobilePlatform);
                if(stats.textureMegabytes.HasValue) stats.textureMegabytes = (float?)Math.Round((double)stats.textureMegabytes, 1, MidpointRounding.AwayFromZero);
                #endif
            }
            else if(obj is SceneAsset)
            {
                GetReferenceFromObject(scaned, SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Component>(true)).ToArray());
            }
            else
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                if(!string.IsNullOrEmpty(assetPath))
                    GetReferenceFromObject(scaned, AssetDatabase.LoadAllAssetsAtPath(assetPath));
            }

            var packagePaths = new HashSet<string>();
            foreach(var o in scaned)
            {
                var path = AssetDatabase.GetAssetPath(o);
                if(path.StartsWith("Packages/"))
                {
                    packagePaths.Add(path.Substring(0, path.IndexOf('/', "Packages/".Length)) + "/package.json");
                }
                else if(path.StartsWith("Assets/"))
                {
                    var packagePath = path.Substring(0, path.LastIndexOf('/')) + "/package.json";
                    while(!File.Exists(packagePath))
                    {
                        packagePath = packagePath.Substring(0, packagePath.LastIndexOf('/'));
                        if(packagePath == "Assets") break;
                        packagePath = packagePath.Substring(0, packagePath.LastIndexOf('/')) + "/package.json";
                    }
                    if(File.Exists(packagePath)) packagePaths.Add(packagePath);
                    else if(!hasRoot || !path.StartsWith(rootpath))
                        scriptPaths.Add(path.Substring(0, path.LastIndexOf('/')) + "/");
                }
            }

            foreach(var packagePath in packagePaths)
            {
                if(!File.Exists(packagePath)) continue;
                var json = File.ReadAllText(packagePath);
                var info = JsonUtility.FromJson<PackageInfo>(json);
                var packageName = string.IsNullOrEmpty(info.displayName) ? info.name : info.displayName;
                if(!string.IsNullOrEmpty(packageName)) packages.Add((packageName, info.version));
            }

            materialCount = scaned.Count(o => o is Material);
            textureCount = scaned.Count(o => o is Texture);
            animCount = scaned.Count(o => o is AnimationClip);
            blendShapeCount = scaned.Where(o => o is Mesh).Sum(o => (o as Mesh).blendShapeCount);

            foreach(var mesh in scaned.Where(o => o is Mesh).Select(o => o as Mesh))
            {
                if(mesh.blendShapeCount == 0) continue;
                var names = Enumerable.Range(0, mesh.blendShapeCount).Where(i =>
                    Enumerable.Range(0, mesh.GetBlendShapeFrameCount(i)).Any(f =>
                    {
                        var deltaVertices = new Vector3[mesh.vertexCount];
                        var deltaNormals = new Vector3[mesh.vertexCount];
                        var deltaTangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(i, f, deltaVertices, deltaNormals, deltaTangents);
                        return deltaVertices.Any(d => d != Vector3.zero) || deltaNormals.Any(d => d != Vector3.zero) || deltaTangents.Any(d => d != Vector3.zero);
                    })
                ).Select(i => mesh.GetBlendShapeName(i));
                blendshapes[mesh.name] = names;
            }

            if(!string.IsNullOrEmpty(productFolder) && Directory.Exists(productFolder))
            {
                var files = Directory.GetFiles(productFolder, "*", SearchOption.AllDirectories);
                texPNG = files.Any(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                texPSD = files.Any(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase));
                texClip = files.Any(f => f.EndsWith(".clip", StringComparison.OrdinalIgnoreCase));
                modelFBX = files.Any(f => f.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
                modelBlend = files.Any(f => f.EndsWith(".blend", StringComparison.OrdinalIgnoreCase));
                modelMB = files.Any(f => f.EndsWith(".mb", StringComparison.OrdinalIgnoreCase));
            }
        }

        private string BuildText()
        {
            bool isFirst = true;
            var sb = new StringBuilder();
            
            sb.AppendLine($"{template.info}");

            sb.AppendLine();
            sb.AppendLine($"{prefix}{template.terms}{postfix}");
            sb.AppendLine(licenseText);

            if(!string.IsNullOrEmpty(trialText))
            {
                sb.AppendLine();
                sb.AppendLine($"{prefix}{template.trial}{postfix}");
                sb.AppendLine(trialText);
            }

            sb.AppendLine();
            sb.AppendLine($"{prefix}{template.unityVersion}{postfix}");
            sb.AppendLine(string.Format(template.unityVersionIn, versionMini, versionFull));

            sb.AppendLine();
            sb.AppendLine($"{prefix}{template.requirements}{postfix}");

            foreach(var package in packages.OrderBy(p => p.Item1)) sb.AppendLine($"{liststyle}{package.Item1} {package.Item2}");
            foreach(var scriptPath in scriptPaths.OrderBy(p => p)) sb.AppendLine($"{liststyle}{scriptPath}");

            sb.AppendLine();
            sb.AppendLine($"{prefix}{template.specifications}{postfix}");
            if(polyCount > 0) sb.AppendLine($"{liststyle}{template.polygons}: {polyCount}");

            void AppendAssets(string name, string unit, int count, int product)
            {
                if(count > 0 && product > 0) sb.AppendLine($"{liststyle}{name}: {count}{unit}{template.bracketL}{string.Format(template.productCount, product, unit)}{template.bracketR}");
                else if(count > 0) sb.AppendLine($"{liststyle}{name}: {count}{unit}");
                else if(product > 0) sb.AppendLine($"{liststyle}{name}: {string.Format(template.productCount, product, unit)}");
            }

            AppendAssets(template.material, template.materialUnit, materialCount, productMaterial);
            AppendAssets(template.texture, template.textureUnit, textureCount, productTexture);
            AppendAssets(template.clip, template.clipUnit, animCount, productAnim);
            AppendAssets(template.shape, template.shapeUnit, blendShapeCount, 0);

            #if LIL_VRCSDK_AVATARS
            AppendAssets(template.expressionParameters, template.expressionParametersUnit, paramsCount, 0);
            #endif

            if(productMesh > 0)
            {
                sb.Append($"{liststyle}{template.modelData}: ");
                isFirst = true;
                if(modelFBX){sb.Append(isFirst ? template.modelData_fbx : $" / {template.modelData_fbx}"); isFirst = false;}
                if(modelBlend){sb.Append(isFirst ? template.modelData_blend : $" / {template.modelData_blend}"); isFirst = false;}
                if(modelMB){sb.Append(isFirst ? template.modelData_mb : $" / {template.modelData_mb}"); isFirst = false;}
                if(!string.IsNullOrEmpty(modelOther)){sb.Append(modelOther); isFirst = false;}
                if(isFirst) sb.Append(template.no);
                sb.AppendLine();
            }

            if(productTexture > 0)
            {
                sb.Append($"{liststyle}{template.textureData}: ");
                isFirst = true;
                if(texPNG){sb.Append(isFirst ? template.textureData_png : $" / {template.textureData_png}"); isFirst = false;}
                if(texPSD){sb.Append(isFirst ? template.textureData_psd : $" / {template.textureData_psd}"); isFirst = false;}
                if(texClip){sb.Append(isFirst ? template.textureData_clip : $" / {template.textureData_clip}"); isFirst = false;}
                if(!string.IsNullOrEmpty(texOther)){sb.Append(texOther); isFirst = false;}
                if(isFirst) sb.Append(template.no);
                sb.AppendLine();
            }

            #if LIL_VRCSDK_AVATARS
            if(stats != null)
            {
                sb.AppendLine();
                sb.AppendLine($"{prefix}{template.performanceRanking}{postfix}");
                foreach(var category in Enum.GetValues(typeof(AvatarPerformanceCategory)) as AvatarPerformanceCategory[])
                {
                    if(category == AvatarPerformanceCategory.AvatarPerformanceCategoryCount || category == AvatarPerformanceCategory.Overall && !isAvatar) continue;

                    if(!showAllStats && (
                        category == AvatarPerformanceCategory.AABB ||
                        category == AvatarPerformanceCategory.AnimatorCount && stats.animatorCount <= 1 || // アバターにはAnimatorが確定で1個あるので省略
                        category == AvatarPerformanceCategory.ParticleTrailsEnabled && stats.particleTrailsEnabled != true ||
                        category == AvatarPerformanceCategory.ParticleCollisionEnabled && stats.particleCollisionEnabled != true ||
                        category == AvatarPerformanceCategory.Overall && !isAvatar
                    )) continue;

                    if(category == AvatarPerformanceCategory.AABB)
                    {
                        sb.AppendLine($"{liststyle}Bounding box (AABB) size: {stats.aabb.GetValueOrDefault().size}");
                        continue;
                    }

                    try
                    {
                        SDKPerformanceDisplay.GetSDKPerformanceInfoText(stats, category, out var text, out var level);
                        if(string.IsNullOrEmpty(text)) continue;
                        var ind = text.IndexOf(" (Recommended");
                        if(ind > 0) text = text.Substring(0,ind);
                        ind = text.IndexOf(" (Maximum");
                        if(ind > 0) text = text.Substring(0,ind);
                        ind = text.IndexOf(" - ");
                        if(ind > 0) text = text.Substring(0,ind);
                        if(!showAllStats && text.EndsWith(" 0")) continue;
                        sb.AppendLine($"{liststyle}{text}");
                    }
                    catch(Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                if(!showAllStats) sb.AppendLine(template.omission);
            }
            #endif

            if(generateBlendShapeList && blendShapeCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{prefix}{template.blendShape}{postfix}");
                isFirst = true;
                foreach(var kv in blendshapes)
                {
                    if(!isFirst) sb.AppendLine();
                    else isFirst = false;

                    sb.AppendLine($"{liststyle}{kv.Key}");
                    foreach(var name in kv.Value) sb.AppendLine($"  - {name}");
                }
            }

            if(!string.IsNullOrEmpty(creditText))
            {
                sb.AppendLine();
                sb.AppendLine($"{prefix}{template.credit}{postfix}");
                sb.AppendLine(creditText);
            }

            sb.AppendLine();
            sb.AppendLine($"{prefix}{template.changelog}{postfix}");

            return sb.ToString();
        }

        private static void GetReferenceFromObject(HashSet<Object> scaned, Object[] objs)
        {
            foreach(var o in objs) GetReferenceFromObject(scaned, o);
        }

        private static void GetReferenceFromObject(HashSet<Object> scaned, Object obj)
        {
            if(!obj || scaned.Contains(obj) ||
                obj is GameObject go && IsEditorOnly(go) ||
                obj is Component c && IsEditorOnly(c)) return;
            scaned.Add(obj);
            if(obj is GameObject ||
                // Skip - Component
                obj is Transform ||
                obj is Cloth ||
                obj is IConstraint ||
                obj is Rigidbody ||
                obj is Joint ||
                // Skip - Asset
                obj is Mesh ||
                obj is Texture ||
                obj is Shader ||
                obj is TextAsset ||
                obj.GetType() == typeof(Object)
            ) return;

            using var so = new SerializedObject(obj);
            using var iter = so.GetIterator();
            var enterChildren = true;
            while(iter.Next(enterChildren))
            {
                enterChildren = iter.propertyType != SerializedPropertyType.String;
                if(iter.propertyType == SerializedPropertyType.ObjectReference)
                {
                    GetReferenceFromObject(scaned, iter.objectReferenceValue);
                }
            }
        }

        private static bool IsEditorOnly(Transform obj)
        {
            if(obj.tag == "EditorOnly") return true;
            if(obj.transform.parent == null) return false;
            return IsEditorOnly(obj.transform.parent);
        }

        private static bool IsEditorOnly(GameObject obj)
        {
            return IsEditorOnly(obj.transform);
        }

        private static bool IsEditorOnly(Component com)
        {
            return IsEditorOnly(com.transform);
        }
    }

    [Serializable]
    internal class PackageInfo
    {
        public string name;
        public string version;
        public string displayName;
    }

    [Serializable]
    internal class Template
    {
        public string info = "⚠必ず利用規約と商品の内容をご確認の上でご購入ください。\r\n⚠Please be sure to check the terms of use and product details before purchasing.";
        public string terms = "利用規約 / Terms of Use";
        public string trial = "試用版 / Trial Version";
        public string credit = "クレジット / Credits";
        public string unityVersion = "Unityバージョン / Unity Version";
        public string unityVersionIn = "Unity {0}（{1}で制作）";
        public string requirements = "前提アセット / Requirements";
        public string specifications = "基本仕様 / Specifications";
        public string polygons = "ポリゴン数";
        public string bracketL = "（";
        public string bracketR = "）";
        public string productCount = "商品フォルダ内に{0}{1}付属";
        public string material = "マテリアル";
        public string materialUnit = "個";
        public string texture = "テクスチャ";
        public string textureUnit = "枚";
        public string clip = "AnimationClip";
        public string clipUnit = "個";
        public string shape = "シェイプキー";
        public string shapeUnit = "個";
        public string modelData = "改変用モデルデータ";
        public string modelData_fbx = ".fbx";
        public string modelData_blend = ".blend";
        public string modelData_mb = ".mb";
        public string textureData = "改変用テクスチャデータ";
        public string textureData_png = ".png";
        public string textureData_psd = ".psd";
        public string textureData_clip = ".clip";
        public string no = "なし";
        public string expressionParameters = "ExpressionParameters";
        public string expressionParametersUnit = " bit";
        public string performanceRanking = "パフォーマンスランク情報 / Performance Ranking";
        public string omission = "※ 未使用の項目は省略しています。";
        public string blendShape = "BlendShape";
        public string changelog = "変更履歴 / Changelog";

        public static Template FromTextAsset(TextAsset textAsset)
        {
            return JsonUtility.FromJson<Template>(textAsset.text);
        }
    }
}
