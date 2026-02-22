#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.VMF
{
#if UNITY_EDITOR
    internal class VPKReader
    {
        private const uint VPK_SIGNATURE = 0x55AA1234;
        private const uint VPK_VERSION1 = 1;
        private const uint VPK_VERSION2 = 2;
        private const ushort TERMINATOR = 0xFFFF;

        private string _vpkPath;
        private string _vpkBaseName;
        private uint _version;
        private uint _treeSize;
        private uint _fileDataSectionSize;
        private uint _archiveMD5SectionSize;
        private uint _otherMD5SectionSize;
        private uint _signatureSectionSize;

        private uint _treeStart;
        private uint _fileDataStart;

        private readonly Dictionary<string, EntryInfo> _fileEntries = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Texture2D> _textureCache = new(StringComparer.OrdinalIgnoreCase);

        public class EntryInfo
        {
            public string Path;
            public string Filename;
            public string Extension;
            public ushort ArchiveIndex;
            public uint EntryOffset;
            public uint EntryLength;
            public ushort Preload;
            public byte[] PreloadData;
            public uint CRC;

            public string GetFullPath()
            {
                return Path + "/" + Filename + "." + Extension;
            }
        }

        public bool Open(string vpkPath)
        {
            if (string.IsNullOrEmpty(vpkPath))
            {
                Debug.LogError("VPK path cannot be null or empty");
                return false;
            }

            try
            {
                if (!File.Exists(vpkPath))
                {
                    Debug.LogError($"VPK file not found: {vpkPath}");
                    return false;
                }

                _vpkPath = vpkPath;

                var fileName = Path.GetFileNameWithoutExtension(vpkPath);
                _vpkBaseName = fileName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - 4)
                    : fileName;

                using (var stream = new FileStream(vpkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
                           FileOptions.SequentialScan))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        if (!ReadVPKHeader(reader)) return false;
                        stream.Position = _treeStart;

                        ReadDirectoryTree(reader);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error initializing VPK reader for '{vpkPath}': {ex.Message}");
                return false;
            }
        }

        private bool ReadVPKHeader(BinaryReader reader)
        {
            try
            {
                var signature = reader.ReadUInt32();
                if (signature != VPK_SIGNATURE)
                {
                    Debug.LogError($"Invalid VPK signature: 0x{signature:X8}, expected 0x{VPK_SIGNATURE:X8}");
                    return false;
                }

                _version = reader.ReadUInt32();
                _treeSize = reader.ReadUInt32();

                if (_treeSize == 0)
                {
                    Debug.LogError("VPK has zero tree size - invalid file");
                    return false;
                }

                _treeStart = 12; // V1 header size

                if (_version == VPK_VERSION2)
                {
                    _fileDataSectionSize = reader.ReadUInt32();
                    _archiveMD5SectionSize = reader.ReadUInt32();
                    _otherMD5SectionSize = reader.ReadUInt32();
                    _signatureSectionSize = reader.ReadUInt32();

                    _treeStart = 28; // V2 header size
                    _fileDataStart = _treeStart + _treeSize;
                }
                else if (_version != VPK_VERSION1)
                {
                    Debug.LogError($"Unsupported VPK version: {_version}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading VPK header: {ex.Message}");
                return false;
            }
        }

        public EntryInfo GetFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            filePath = filePath.ToLowerInvariant().Replace('\\', '/').TrimStart('/');

            if (_fileEntries.TryGetValue(filePath, out var directMatch)) return directMatch; // CACHE
            return FindFileWithFallbacks(filePath);
        }

        private EntryInfo FindFileWithFallbacks(string normalizedPath)
        {
            if (!normalizedPath.EndsWith(".vtf", StringComparison.Ordinal))
            {
                var vtfPath = normalizedPath + ".vtf";
                if (_fileEntries.TryGetValue(vtfPath, out var vtfMatch)) return vtfMatch;
            }

            if (!normalizedPath.StartsWith("materials/", StringComparison.Ordinal))
            {
                var materialsPath = "materials/" + normalizedPath;
                if (_fileEntries.TryGetValue(materialsPath, out var materialsMatch)) return materialsMatch;

                if (!materialsPath.EndsWith(".vtf", StringComparison.Ordinal))
                {
                    var materialsVtfPath = materialsPath + ".vtf";
                    if (_fileEntries.TryGetValue(materialsVtfPath, out var materialsVtfMatch)) return materialsVtfMatch;
                }
            }

            return null;
        }

        public Texture2D LoadTexture(EntryInfo entry)
        {
            if (entry == null) return GetErrorTexture();

            var path = entry.GetFullPath();
            if (_textureCache.TryGetValue(path, out var cachedTexture))
            {
                if (cachedTexture) return cachedTexture;
                _textureCache.Remove(path);
            }

            try
            {
                var fileData = ExtractFile(path);
                if (fileData is not { Length: > 0 })
                {
                    Debug.LogError($"VTF file is empty or could not be extracted: {path}");
                    return GetErrorTexture();
                }

                var texture = LoadVTFTexture(path, fileData);
                if (!texture)
                {
                    Debug.LogError($"Failed to parse VTF texture: {path}");
                    return GetErrorTexture();
                }

                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Repeat;

                _textureCache[path] = texture;
                return texture;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading VTF texture '{path}': {ex.Message}");
                return GetErrorTexture();
            }
        }

        public Texture2D LoadTexture(string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath)) return GetErrorTexture();

            var entry = GetFile(texturePath);
            if (entry != null) return LoadTexture(entry);

            Debug.LogError($"VTF texture not found in VPK: '{texturePath}'");
            return GetErrorTexture();
        }

        private byte[] ExtractFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            var normalizedPath = filePath.ToLowerInvariant();
            if (!_fileEntries.TryGetValue(normalizedPath, out var entry))
            {
                Debug.LogWarning($"File not found in VPK directory: {filePath}");
                return null;
            }

            try
            {
                if (entry.PreloadData != null && entry.EntryLength == 0) return entry.PreloadData;

                var archivePath = GetArchivePath(entry);
                if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                {
                    Debug.LogError($"VPK archive not found: {archivePath}");
                    return null;
                }

                var mainData = ReadFileFromArchive(archivePath, entry);
                if (mainData == null) return null;

                return CombineFileData(entry.PreloadData, mainData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error extracting file '{filePath}': {ex.Message}");
                return null;
            }
        }

        private string GetArchivePath(EntryInfo entry)
        {
            if (entry.ArchiveIndex == 0x7FFF) return _vpkPath;

            var directory = Path.GetDirectoryName(_vpkPath);
            if (string.IsNullOrEmpty(directory)) directory = Environment.CurrentDirectory;

            return Path.Combine(directory, $"{_vpkBaseName}_{entry.ArchiveIndex:D3}.vpk");
        }

        private byte[] ReadFileFromArchive(string archivePath, EntryInfo entry)
        {
            try
            {
                using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192))
                {
                    var filePosition = entry.EntryOffset;
                    if (entry.ArchiveIndex == 0x7FFF && _version == VPK_VERSION2)
                        filePosition = _fileDataStart + entry.EntryOffset;

                    if (filePosition >= stream.Length)
                    {
                        Debug.LogError(
                            $"Invalid file offset {filePosition} in archive {archivePath} (length: {stream.Length})");
                        return null;
                    }

                    stream.Position = filePosition;
                    return ReadFileData(stream, entry.EntryLength);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading from archive '{archivePath}': {ex.Message}");
                return null;
            }
        }

        private byte[] ReadFileData(Stream stream, uint expectedLength)
        {
            if (expectedLength == 0) return Array.Empty<byte>();

            var fileData = new byte[expectedLength];
            var totalBytesRead = 0;
            var remaining = (int)expectedLength;

            while (remaining > 0)
            {
                var bytesRead = stream.Read(fileData, totalBytesRead, remaining);
                if (bytesRead <= 0)
                {
                    Debug.LogWarning(
                        $"Unexpected end of stream. Expected {expectedLength} bytes, got {totalBytesRead}");
                    break;
                }

                totalBytesRead += bytesRead;
                remaining -= bytesRead;
            }

            if (totalBytesRead != expectedLength)
            {
                Debug.LogWarning($"Incomplete file read: expected {expectedLength} bytes, got {totalBytesRead}");

                var partialData = new byte[totalBytesRead];
                Buffer.BlockCopy(fileData, 0, partialData, 0, totalBytesRead);
                return partialData;
            }

            return fileData;
        }

        private byte[] CombineFileData(byte[] preloadData, byte[] mainData)
        {
            if (preloadData?.Length > 0)
            {
                var combinedData = new byte[preloadData.Length + mainData.Length];
                Buffer.BlockCopy(preloadData, 0, combinedData, 0, preloadData.Length);
                Buffer.BlockCopy(mainData, 0, combinedData, preloadData.Length, mainData.Length);
                return combinedData;
            }

            return mainData;
        }

        private Texture2D LoadVTFTexture(string path, byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(ms))
                {
                    try
                    {
                        // SIGNATURE
                        var signature = reader.ReadBytes(4);
                        if (signature[0] != 'V' || signature[1] != 'T' || signature[2] != 'F' || signature[3] != 0)
                        {
                            Debug.LogError($"Invalid VTF signature for {path}");
                            return GetErrorTexture();
                        }

                        // VERSION
                        var majorVersion = reader.ReadUInt32();
                        var minorVersion = reader.ReadUInt32();

                        // HEADER
                        var headerSize = reader.ReadUInt32();

                        // DIMENSIONS
                        var width = reader.ReadUInt16();
                        var height = reader.ReadUInt16();

                        if (width == 0 || height == 0 || width > 8192 || height > 8192)
                        {
                            Debug.LogError($"Invalid VTF size for {path}");
                            return GetErrorTexture();
                        }

                        // FLAGS
                        var flags = reader.ReadUInt32();
                        var hasAlpha = (flags & 0x2) != 0;

                        // FRAMES
                        var frames = reader.ReadUInt16();
                        var firstFrame = reader.ReadUInt16();

                        reader.BaseStream.Position += 4;
                        reader.BaseStream.Position += 12; // 3 floats (12 bytes)
                        reader.BaseStream.Position += 4;

                        // BUMPSCALE
                        var bumpScale = reader.ReadSingle();

                        // IMAGE FORMAT
                        var imageFormat = reader.ReadUInt32();

                        // MIPMAP COUNT
                        var mipmapCount = reader.ReadByte();
                        if (mipmapCount == 0) mipmapCount = 1; // Ensure at least one mip level

                        var lowResImageFormat = reader.ReadUInt32();
                        var lowResImageWidth = reader.ReadByte();
                        var lowResImageHeight = reader.ReadByte();

                        ushort depth = 1;
                        if (majorVersion >= 7 && minorVersion >= 2) depth = reader.ReadUInt16();

                        var lowResImageSize = GetImageSize(lowResImageFormat, lowResImageWidth, lowResImageHeight);
                        if (majorVersion >= 7 && minorVersion >= 3)
                        {
                            ms.Position = 68;
                            var numResources = reader.ReadUInt32();
                            // Each entry: 3 bytes tag + 1 byte flags + 4 bytes offset = 8 bytes
                            // Resource entries start at byte 80
                            long resourceDirPos = 80;

                            uint highResOffset = 0;
                            for (uint r = 0; r < numResources && r < 32; r++)
                            {
                                ms.Position = resourceDirPos + r * 8;
                                var tag0 = reader.ReadByte();
                                var tag1 = reader.ReadByte();
                                var tag2 = reader.ReadByte();
                                var resFlags = reader.ReadByte();
                                var offset = reader.ReadUInt32();

                                // 0x30 = high-res image data
                                if (tag0 == 0x30 && tag1 == 0x00 && tag2 == 0x00)
                                {
                                    highResOffset = offset;
                                    break;
                                }
                            }

                            ms.Position = highResOffset > 0
                                ? highResOffset
                                : headerSize + lowResImageSize; // Fallback
                        }
                        else
                        {
                            ms.Position = headerSize + lowResImageSize;
                        }

                        var unityFormat = GetUnityTextureFormat(imageFormat, hasAlpha);
                        if (unityFormat == TextureFormat.RGBA32 && !IsFormatSupported(imageFormat))
                        {
                            Debug.LogWarning($"Unsupported VTF format: {imageFormat}, using fallback");
                            return GetErrorTexture();
                        }

                        // DXT
                        if (unityFormat == TextureFormat.DXT1 || unityFormat == TextureFormat.DXT5)
                            if (width % 4 != 0 || height % 4 != 0)
                            {
                                Debug.LogError(
                                    $"VTF DXT texture '{Path.GetFileName(path)}' has dimensions ({width}x{height}) which are not a multiple of 4. Unity requires DXT textures to have dimensions that are multiples of 4. Skipping texture.");
                                return GetErrorTexture();
                            }

                        var texture = new Texture2D(width, height, unityFormat, mipmapCount > 1)
                        {
                            name = Path.GetFileName(path),
                            alphaIsTransparency = hasAlpha
                        };

                        byte[] imageData;
                        if (unityFormat is TextureFormat.DXT1 or TextureFormat.DXT5)
                        {
                            // VTF stores mipmaps from smallest to largest
                            // Unity expects largest to smallest, so we must reverse the order
                            var mipSizes = new int[mipmapCount];
                            var totalSize = 0;
                            for (var i = 0; i < mipmapCount; i++)
                            {
                                var mipWidth = Math.Max(1, width >> i);
                                var mipHeight = Math.Max(1, height >> i);
                                mipSizes[i] = GetMipMapSize(imageFormat, mipWidth, mipHeight);
                                totalSize += mipSizes[i];
                            }

                            var rawMipData = reader.ReadBytes(totalSize);

                            if (mipmapCount > 1)
                            {
                                // Reverse mip order: VTF is smallest-first, Unity needs largest-first
                                imageData = new byte[totalSize];
                                var srcOffset = totalSize; // Start from end (largest mip in VTF)
                                var dstOffset = 0;

                                for (var i = 0; i < mipmapCount; i++)
                                {
                                    srcOffset -= mipSizes[i];
                                    Buffer.BlockCopy(rawMipData, srcOffset, imageData, dstOffset, mipSizes[i]);
                                    dstOffset += mipSizes[i];
                                }
                            }
                            else
                            {
                                imageData = rawMipData;
                            }

                            texture.LoadRawTextureData(imageData);
                        }
                        else
                        {
                            var dataSize = width * height * 4; // RGBA32 = 4 bytes per pixel
                            imageData = new byte[dataSize];

                            var mipSize = GetMipMapSize(imageFormat, width, height);
                            var mipData = reader.ReadBytes(mipSize);

                            ConvertToRGBA(mipData, imageData, imageFormat, width, height, hasAlpha);
                            texture.SetPixelData(imageData, 0);
                        }

                        texture.Apply(mipmapCount > 1, false);
                        return texture;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error parsing VTF texture: {ex.Message}");
                        return GetErrorTexture();
                    }
                }
            }
        }

        public void Close()
        {
            foreach (var texture in _textureCache.Values)
                if (texture)
                    Object.DestroyImmediate(texture);

            _textureCache.Clear();
            _fileEntries.Clear();

            Debug.Log($"Closed VPK: {_vpkPath}");
        }

        #region PRIVATE

        private void ReadDirectoryTree(BinaryReader reader)
        {
            _fileEntries.Clear();

            try
            {
                var pathBuilder = new StringBuilder(512);
                // extension -> directory -> filename
                while (true)
                {
                    var extension = ReadNullTerminatedString(reader);
                    if (string.IsNullOrEmpty(extension)) break; // End of extensions

                    var isVTF = extension.Equals("vtf", StringComparison.OrdinalIgnoreCase);
                    while (true)
                    {
                        var directoryPath = ReadNullTerminatedString(reader);
                        if (string.IsNullOrEmpty(directoryPath)) break;

                        while (true)
                        {
                            var filename = ReadNullTerminatedString(reader);
                            if (string.IsNullOrEmpty(filename)) break;

                            try
                            {
                                var entry = ReadFileEntry(reader, extension, directoryPath, filename);
                                if (entry == null) continue;

                                if (isVTF)
                                {
                                    var filePath = BuildFilePath(pathBuilder, directoryPath, filename, extension);
                                    _fileEntries[filePath] = entry;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError(
                                    $"Error reading file entry '{filename}' in '{directoryPath}': {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading VPK directory tree: {ex.Message}");
                throw;
            }
        }

        private EntryInfo ReadFileEntry(BinaryReader reader, string extension, string directoryPath, string filename)
        {
            try
            {
                var entry = new EntryInfo
                {
                    Extension = extension,
                    Path = directoryPath,
                    Filename = filename,
                    CRC = reader.ReadUInt32()
                };

                var preloadBytes = reader.ReadUInt16();
                entry.ArchiveIndex = reader.ReadUInt16();
                entry.EntryOffset = reader.ReadUInt32();
                entry.EntryLength = reader.ReadUInt32();

                var terminator = reader.ReadUInt16();
                if (terminator != TERMINATOR)
                    Debug.LogWarning($"Unexpected terminator value: 0x{terminator:X4} (expected 0x{TERMINATOR:X4})");

                if (preloadBytes > 0)
                {
                    entry.Preload = preloadBytes;
                    entry.PreloadData = reader.ReadBytes(preloadBytes);

                    if (entry.PreloadData.Length != preloadBytes)
                        Debug.LogWarning(
                            $"Incomplete preload data read for '{filename}': expected {preloadBytes}, got {entry.PreloadData.Length}");
                }

                return entry;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading file entry data: {ex.Message}");
                return null;
            }
        }

        private string BuildFilePath(StringBuilder builder, string directory, string filename, string extension)
        {
            builder.Clear();

            if (!string.IsNullOrEmpty(directory) && directory.Trim() != "")
            {
                builder.Append(directory.ToLowerInvariant());
                builder.Append('/');
            }

            builder.Append(filename.ToLowerInvariant());
            if (string.IsNullOrEmpty(extension)) return builder.ToString();

            builder.Append('.');
            builder.Append(extension.ToLowerInvariant());

            return builder.ToString();
        }

        private string ReadNullTerminatedString(BinaryReader reader)
        {
            try
            {
                var bytes = new List<byte>(64);

                byte b;
                while ((b = reader.ReadByte()) != 0)
                {
                    bytes.Add(b);
                    if (bytes.Count <= 1024) continue;

                    Debug.LogWarning("Abnormally long string detected in VPK, truncating");
                    break;
                }

                return bytes.Count == 0 ? string.Empty : Encoding.UTF8.GetString(bytes.ToArray());
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading null-terminated string: {ex.Message}");
                return string.Empty;
            }
        }

        private bool IsFormatSupported(uint format)
        {
            switch (format)
            {
                case 0: // RGBA8888
                case 2: // ABGR8888
                case 3: // RGB888
                case 4: // BGR888
                case 12: // BGRA8888
                case 13: // DXT1
                case 14: // DXT3
                case 15: // DXT5
                    return true;
                default:
                    return false;
            }
        }

        // Convert to RGBA format for Unity
        private void ConvertToRGBA(byte[] source, byte[] dest, uint format, int width, int height, bool hasAlpha)
        {
            var pixelCount = width * height;

            switch (format)
            {
                case 0: // RGBA8888
                    Buffer.BlockCopy(source, 0, dest, 0, Math.Min(source.Length, dest.Length));
                    break;

                case 2: // ABGR8888
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var srcIdx = i * 4;
                        var dstIdx = i * 4;

                        if (srcIdx + 3 < source.Length && dstIdx + 3 < dest.Length)
                        {
                            dest[dstIdx] = source[srcIdx + 3]; // R = A
                            dest[dstIdx + 1] = source[srcIdx + 2]; // G = B
                            dest[dstIdx + 2] = source[srcIdx + 1]; // B = G
                            dest[dstIdx + 3] = source[srcIdx]; // A = R
                        }
                    }

                    break;

                case 12: // BGRA8888
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var srcIdx = i * 4;
                        var dstIdx = i * 4;

                        if (srcIdx + 3 < source.Length && dstIdx + 3 < dest.Length)
                        {
                            dest[dstIdx] = source[srcIdx + 2]; // R = B
                            dest[dstIdx + 1] = source[srcIdx + 1]; // G = G
                            dest[dstIdx + 2] = source[srcIdx]; // B = R
                            dest[dstIdx + 3] = source[srcIdx + 3]; // A = A
                        }
                    }

                    break;

                case 3: // RGB888
                case 4: // BGR888
                    var isBGR = format == 4;
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var srcIdx = i * 3;
                        var dstIdx = i * 4;

                        if (srcIdx + 2 < source.Length && dstIdx + 3 < dest.Length)
                        {
                            if (isBGR)
                            {
                                dest[dstIdx] = source[srcIdx + 2]; // R = B
                                dest[dstIdx + 1] = source[srcIdx + 1]; // G = G
                                dest[dstIdx + 2] = source[srcIdx]; // B = R
                            }
                            else
                            {
                                dest[dstIdx] = source[srcIdx]; // R = R
                                dest[dstIdx + 1] = source[srcIdx + 1]; // G = G
                                dest[dstIdx + 2] = source[srcIdx + 2]; // B = B
                            }

                            dest[dstIdx + 3] = 255; // A
                        }
                    }

                    break;

                default:
                    // Fill with magenta for unsupported formats
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var dstIdx = i * 4;
                        if (dstIdx + 3 < dest.Length)
                        {
                            dest[dstIdx] = 255; // R
                            dest[dstIdx + 1] = 0; // G
                            dest[dstIdx + 2] = 255; // B
                            dest[dstIdx + 3] = 255; // A
                        }
                    }

                    break;
            }
        }

        private int GetImageSize(uint format, int width, int height)
        {
            switch (format)
            {
                case 13: // DXT1
                    return Math.Max(8, (width + 3) / 4 * ((height + 3) / 4) * 8);
                case 14: // DXT3
                case 15: // DXT5
                    return Math.Max(16, (width + 3) / 4 * ((height + 3) / 4) * 16);
                case 3: // RGB888
                case 4: // BGR888
                    return width * height * 3;
                case 0: // RGBA8888
                case 2: // ABGR8888
                case 12: // BGRA8888
                    return width * height * 4;
                default:
                    return width * height * 4;
            }
        }

        private TextureFormat GetUnityTextureFormat(uint vtfFormat, bool hasAlpha)
        {
            switch (vtfFormat)
            {
                case 0: // RGBA8888
                case 2: // ABGR8888
                case 3: // RGB888
                case 4: // BGR888
                case 12: // BGRA8888
                    return TextureFormat.RGBA32;
                case 13:
                    return TextureFormat.DXT1;
                case 14: // DXT3 - Unity prefers DXT5
                case 15:
                    return TextureFormat.DXT5;
                default:
                    return TextureFormat.RGBA32;
            }
        }

        private int GetMipMapSize(uint format, int width, int height)
        {
            return GetImageSize(format, width, height);
        }

        #region ERROR TEXTURE

        private Texture2D GetErrorTexture()
        {
            if (_textureCache.TryGetValue("__ERROR__", out var value)) return value;

            var texture = CreateErrorTexture();
            _textureCache["__ERROR__"] = texture;

            return texture;
        }

        public static Texture2D CreateErrorTexture()
        {
            const int SIZE = 32;

            var texture = new Texture2D(SIZE, SIZE, TextureFormat.RGB24, false)
            {
                name = "INVALID-TEXTURE"
            };

            var colors = new Color32[SIZE * SIZE];
            var color1 = new Color32(200, 0, 200, 255);
            var color2 = new Color32(1, 1, 1, 255);

            for (var y = 0; y < SIZE; y++)
            for (var x = 0; x < SIZE; x++)
            {
                var isCheckerboard = (x / 8 + y / 8) % 2 == 0;
                colors[y * SIZE + x] = isCheckerboard ? color1 : color2;
            }

            texture.alphaIsTransparency = false;
            texture.filterMode = FilterMode.Point;

            texture.SetPixels32(colors);
            texture.Apply();

            return texture;
        }

        #endregion

        #endregion
    }

#endif
}

/*# MIT License Copyright (c) 2026 FailCake

# Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
# "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
# distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
# the following conditions:
#
# The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
#
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
# MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
# ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
# SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.*/