using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SpriteCutterPrefabCreator
{
    private const string PrefabFolderPath = "Assets/Prefabs";
    
    public static void BuildPrefabFromCutPieces(string fileName, List<CutPieceData> pieces)
    {
        if (pieces == null || pieces.Count == 0) return;
        
        GameObject root = new GameObject($"PRF_{fileName}");

        foreach (CutPieceData piece in pieces)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(piece.path);
            if (sprite == null) continue;
            
            GameObject child = new GameObject(sprite.name);
            child.transform.SetParent(root.transform);
            
            child.AddComponent<SpriteRenderer>().sprite = sprite;
            float ppu = sprite.pixelsPerUnit;
            float pivotX = sprite.pivot.x;
            float pivotY = sprite.pivot.y;

            float localX = (piece.pivotOffset.x + sprite.pivot.x) / ppu;
            float localY = (piece.pivotOffset.y + sprite.pivot.y) / ppu;
            
            child.transform.localPosition = new Vector3(localX, localY, 0);
            child.transform.localRotation = Quaternion.identity;
        }
        
        string prefabPath = $"{PrefabFolderPath}/{fileName}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        
        Object.DestroyImmediate(root);
        
        Debug.Log($"Generated {fileName}.prefab");
    } 
}
