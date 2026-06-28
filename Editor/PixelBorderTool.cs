using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class PixelBorderTool : EditorWindow
{
    enum BorderMode
    {
        FourDirections,
        EightDirections,
    }

    static readonly string[] _supportedExt =
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tga",
        ".tif",
        ".tiff",
    };

    Color _borderColor = Color.black;
    BorderMode _borderMode = BorderMode.FourDirections;

    // path -> original file bytes
    readonly Dictionary<string, byte[]> _backups = new();
    List<string> _targetPaths = new();

    [MenuItem("Assets/點陣外框工具", false, 1000)]
    static void OpenWindow()
    {
        var win = GetWindow<PixelBorderTool>("點陣外框工具");
        win.minSize = new Vector2(280, 160);
        win._targetPaths = CollectImagePaths();
        win._backups.Clear();
        win.Show();
    }

    [MenuItem("Assets/點陣外框工具", true)]
    static bool OpenWindowValidate() => CollectImagePaths().Count > 0;

    static List<string> CollectImagePaths()
    {
        var paths = new List<string>();
        foreach (var obj in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(_supportedExt, ext) >= 0)
                paths.Add(path);
        }
        return paths;
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"已選取 {_targetPaths.Count} 張圖片", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _borderColor = EditorGUILayout.ColorField("外框顏色", _borderColor);
        _borderMode = (BorderMode)EditorGUILayout.EnumPopup("外框方位", _borderMode);

        EditorGUILayout.Space(12);

        if (GUILayout.Button("套用", GUILayout.Height(28)))
            Apply();

        GUI.enabled = _backups.Count > 0;
        if (GUILayout.Button("復原", GUILayout.Height(28)))
            Revert();
        GUI.enabled = true;
    }

    void Apply()
    {
        if (_targetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("錯誤", "沒有選取任何圖片。", "OK");
            return;
        }

        _backups.Clear();
        int successCount = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var path in _targetPaths)
            {
                var abs = Path.GetFullPath(path);

                // backup original bytes
                _backups[path] = File.ReadAllBytes(abs);

                // temporarily make readable
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                bool wasReadable = false;
                TextureImporterCompression origCompression =
                    TextureImporterCompression.Uncompressed;
                if (importer != null)
                {
                    wasReadable = importer.isReadable;
                    origCompression = importer.textureCompression;
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                try
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(_backups[path]);

                    var pixels = tex.GetPixels32();
                    int w = tex.width,
                        h = tex.height;
                    var result = (Color32[])pixels.Clone();

                    Color32 border = _borderColor;
                    border.a = 255;

                    int[] dx4 = { 0, 0, -1, 1 };
                    int[] dy4 = { 1, -1, 0, 0 };
                    int[] dx8 = { 0, 0, -1, 1, -1, 1, -1, 1 };
                    int[] dy8 = { 1, -1, 0, 0, 1, 1, -1, -1 };

                    int[] dx = _borderMode == BorderMode.EightDirections ? dx8 : dx4;
                    int[] dy = _borderMode == BorderMode.EightDirections ? dy8 : dy4;

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            if (pixels[y * w + x].a == 0)
                            {
                                // if any neighbour is non-transparent, this pixel becomes border
                                bool hasSolidNeighbour = false;
                                for (int d = 0; d < dx.Length; d++)
                                {
                                    int nx = x + dx[d],
                                        ny = y + dy[d];
                                    if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                                        continue;
                                    if (pixels[ny * w + nx].a > 0)
                                    {
                                        hasSolidNeighbour = true;
                                        break;
                                    }
                                }
                                if (hasSolidNeighbour)
                                    result[y * w + x] = border;
                            }
                        }
                    }

                    tex.SetPixels32(result);
                    tex.Apply();
                    File.WriteAllBytes(abs, tex.EncodeToPNG());
                    DestroyImmediate(tex);
                    successCount++;
                }
                finally
                {
                    // restore importer settings
                    if (importer != null)
                    {
                        importer.isReadable = wasReadable;
                        importer.textureCompression = origCompression;
                        importer.SaveAndReimport();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("錯誤", ex.Message, "OK");
            return;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog("成功", $"已成功處理 {successCount} 張圖片。", "OK");
        Repaint();
    }

    void Revert()
    {
        if (_backups.Count == 0)
        {
            EditorUtility.DisplayDialog("錯誤", "沒有可復原的備份。", "OK");
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var kv in _backups)
            {
                var abs = Path.GetFullPath(kv.Key);
                File.WriteAllBytes(abs, kv.Value);
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("錯誤", ex.Message, "OK");
            return;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        int count = _backups.Count;
        _backups.Clear();
        EditorUtility.DisplayDialog("成功", $"已成功復原 {count} 張圖片。", "OK");
        Repaint();
    }
}
