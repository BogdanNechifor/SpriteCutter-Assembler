using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using Unity.Collections;

public sealed class SpriteCutterTool
{
    
    //settings
    private const int MaxRawChunkSize = 2040;
    private const int ChunkPadding = 4;
    private const int TrimPadding = 4;
    
    
    [MenuItem("Assets/Sprite Tools/Sprite Cutter (2K)")]
    public static void CutTexture()
    {
        Texture2D sourceTex = Selection.activeObject as Texture2D;
        if (sourceTex == null)
        {
            Debug.LogError("Selected file is not a Texture2D.");
            return;
        }
        CutTexture(sourceTex, false);
    }

    [MenuItem("Assets/Sprite Tools/Sprite Cutter (2K) + Prefab Creator")]
    public static void CutTextureAndCreatePrefab()
    {
        Texture2D sourceTex = Selection.activeObject as Texture2D;
        if (sourceTex == null)
        {
            Debug.LogError("Selected file is not a Texture2D.");
            return;
        }
        CutTexture(sourceTex, true);
    }
    
    private static void CutTexture(Texture2D sourceTex, bool passToPrefabCreator)
    {
        string assetPath = AssetDatabase.GetAssetPath(sourceTex);
        
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        NativeArray<Color32> rawTex = sourceTex.GetRawTextureData<Color32>();
        int texWidth = sourceTex.width;
        int texHeight = sourceTex.height;
        
        
        //initial visible pixels check
        RectInt wholeTexArea = new RectInt(0, 0, texWidth, texHeight);
        RectInt texBounds = GetTrimmedBounds(rawTex, texWidth, wholeTexArea);
        if (texBounds.height <= 0 || texBounds.width <= 0)
        {
            Debug.LogWarning($"Texture: {sourceTex} has no visible pixels. Cut aborted.");
            return;
        }
        
        List<CutPieceData> pieces = new();
        string folderPath = Path.GetDirectoryName(assetPath);
        string fileName = Path.GetFileNameWithoutExtension(assetPath);


        for (int y = texBounds.y; y < texBounds.yMax; y += MaxRawChunkSize)
        {
            //allow it to have a height <2048px
            int rowHeight = Mathf.Min(MaxRawChunkSize, texBounds.yMax - y);

            RectInt rowArea = new RectInt(texBounds.x, y, texBounds.width, rowHeight);

            //trim the row, ignore it if empty 
            RectInt trimmedRowArea = GetTrimmedBounds(rawTex, texWidth, rowArea);
            if (trimmedRowArea.width <= 0 || trimmedRowArea.height <= 0) continue;

            for (int x = trimmedRowArea.x; x < trimmedRowArea.xMax; x += MaxRawChunkSize)
            {
                int colWidth = Mathf.Min(MaxRawChunkSize, trimmedRowArea.xMax - x);

                RectInt colArea = new RectInt(x, trimmedRowArea.y, colWidth, trimmedRowArea.height);

                //final trim, ignore if it results in an empty cell
                RectInt finalBounds = GetTrimmedBounds(rawTex, texWidth, colArea);
                if (finalBounds.width <= 0 || finalBounds.height <= 0) continue;

                CutPieceData piece = ProcessCutPiece(rawTex, texWidth, texHeight, finalBounds, folderPath, fileName, pieces.Count);
                pieces.Add(piece);
            }
        }
        
        AssetDatabase.Refresh();
        ApplyImportSettingsToPieces(pieces);
        
        Debug.Log($"Cut {fileName} into {pieces.Count} pieces.");
        if(passToPrefabCreator) SpriteCutterPrefabCreator.BuildPrefabFromCutPieces(fileName, pieces);
        
    }

    private static CutPieceData ProcessCutPiece(NativeArray<Color32> rawTex, int texWidth, int texHeight, RectInt bounds, string folderPath, string fileName, int index)
    {
        //adding padding so the seams are not visible in game
        int startX = Mathf.Max(0, bounds.x - ChunkPadding);
        int paddedWidth = Mathf.Min(bounds.xMax + ChunkPadding, texWidth) - startX;
        
        int startY = Mathf.Max(0, bounds.y - ChunkPadding);
        int paddedHeight = Mathf.Min(bounds.yMax + ChunkPadding, texHeight) - startY;
        
        //making sure it allows 4x4 block compression
        int correctedWidth = (paddedWidth % 4 != 0) ? paddedWidth + 4 - paddedWidth % 4 : paddedWidth;
        int correctedHeight = (paddedHeight % 4 != 0) ? paddedHeight + 4 - paddedHeight % 4 : paddedHeight;
        
        //create clear destination array
        Color32[] piecePixelArray = new Color32[correctedWidth * correctedHeight];
        for (int i =0; i < piecePixelArray.Length; i++) piecePixelArray[i] = Color.clear;

        //padded is safe to check, corrected might not be, iterate on padded
        for (int y = 0; y < paddedHeight; y++)
        {
            int texY = startY + y;
            if (texY < 0 || texY >= texHeight) continue;
            
            for (int x = 0; x < paddedWidth; x++)
            {
                int texX = startX + x;
                if (texX < 0 || texX >= texWidth) continue;
                
                int rawIndex = texX + texY * texWidth;
                int pieceIndex = y * correctedWidth + x;
                
                //grab color data from source texture
                if (rawIndex >= 0 && rawIndex < rawTex.Length)
                {
                    piecePixelArray[pieceIndex] = rawTex[rawIndex];
                }
                
            }
        }
        
        //create and save the texture piece
        Texture2D cutPieceTex = new  Texture2D(correctedWidth, correctedHeight, TextureFormat.RGBA32, false);
        cutPieceTex.SetPixels32(piecePixelArray);
        cutPieceTex.Apply();
        
        
        if (!Directory.Exists($"{folderPath}/{fileName}"))
        {
            Directory.CreateDirectory($"{folderPath}/{fileName}");
        }
        
        string pieceFilePath = $"{folderPath}/{fileName}/{fileName}_{index:D2}.png";
        File.WriteAllBytes(pieceFilePath, cutPieceTex.EncodeToPNG());
        
        
        CutPieceData cutPiece = new()
        {
            path = pieceFilePath,
            pivotOffset = new Vector2(startX, startY),
        };
        
        return cutPiece;
    }

    private static void ApplyImportSettingsToPieces(List<CutPieceData> pieces)
    {
        foreach (CutPieceData piece in pieces)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(piece.path);

            if (importer)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
        }
    }
    
    private static RectInt GetTrimmedBounds(NativeArray<Color32> rawTex, int texWidth, RectInt area)
    {
        int minX = area.width;
        int minY = area.height;
        int maxX = -1;
        int maxY = -1;
        bool foundPixel = false;

        
        int texHeight = rawTex.Length / texWidth;
        
        for (int y = 0; y < area.height; y++)
        {
            int texY = area.y + y;
            if (texY < 0 || texY >= texHeight) continue;
            
            for (int x = 0; x < area.width; x++)
            {
                int texX = area.x + x;
                if (texX < 0 || texX >= texWidth) continue;
                
                int index = texX + texY * texWidth;
                
                
                if (index < 0 || index >= rawTex.Length) continue;
                //find visible pixels
                if (rawTex[index].a > 5)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    foundPixel = true;
                }
            }
        }
        
        //empty tex
        if(!foundPixel) return new RectInt(0,0,0,0);
        
        
        
        minX = minX + area.x - TrimPadding;
        minY = minY + area.y - TrimPadding;
        maxX = maxX + area.x + TrimPadding;
        maxY = maxY + area.y + TrimPadding;
        
        minX = Mathf.Max(minX, area.x);
        maxX = Mathf.Min(maxX, area.xMax - 1);
        minY = Mathf.Max(minY, area.y);
        maxY = Mathf.Min(maxY, area.yMax - 1);
        
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}

public sealed class CutPieceData
{
    public string path;
    public Vector2 pivotOffset;
        
}