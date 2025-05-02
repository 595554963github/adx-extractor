using System;
using System.Collections.Generic;
using System.IO;

namespace CodeTranslation
{
    public struct AdxExtractStruct
    {
        public string SourceFolderPath { get; set; }
    }

    class AdxExtractor
    {
        // ADX 文件特征字节序列
        static readonly byte[] ADX_SIG_BYTES = new byte[] { 0x80, 0x00 };
        // CRI 版权信息字节序列
        static readonly byte[] CRI_COPYRIGHT_BYTES = new byte[] { 0x28, 0x63, 0x29, 0x43, 0x52, 0x49 };
        // 固定序列用于进一步验证
        static readonly byte[][] FIXED_SEQUENCES =
        {
            new byte[] { 0x03, 0x12, 0x04, 0x01, 0x00, 0x00 },
            new byte[] { 0x03, 0x12, 0x04, 0x02, 0x00, 0x00 }
        };

        private static int[] ComputeLPSArray(byte[] pattern)
        {
            int[] lps = new int[pattern.Length];
            int len = 0;
            lps[0] = 0;

            int i = 1;
            while (i < pattern.Length)
            {
                if (pattern[i] == pattern[len])
                {
                    len++;
                    lps[i] = len;
                    i++;
                }
                else
                {
                    if (len != 0)
                    {
                        len = lps[len - 1];
                    }
                    else
                    {
                        lps[i] = 0;
                        i++;
                    }
                }
            }
            return lps;
        }

        private static int FindBytes(byte[] data, int startIndex, byte[] pattern)
        {
            int[] lps = ComputeLPSArray(pattern);
            int i = startIndex;
            int j = 0;

            while (i < data.Length)
            {
                if (pattern[j] == data[i])
                {
                    i++;
                    j++;
                }

                if (j == pattern.Length)
                {
                    return i - j;
                }
                else if (i < data.Length && pattern[j] != data[i])
                {
                    if (j != 0)
                    {
                        j = lps[j - 1];
                    }
                    else
                    {
                        i++;
                    }
                }
            }
            return -1;
        }

        private static bool ContainsBytes(byte[] data, byte[] pattern)
        {
            return FindBytes(data, 0, pattern) != -1;
        }

        public List<(string FileName, byte[] Content, int Count)> ExtractAdxFiles(AdxExtractStruct extractStruct)
        {
            var extractedFiles = new List<(string FileName, byte[] Content, int Count)>();
            string sourceFolder = extractStruct.SourceFolderPath;

            if (!Directory.Exists(sourceFolder))
            {
                Console.WriteLine($"源文件夹 {sourceFolder} 不存在");
                return extractedFiles;
            }

            foreach (var filePath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(filePath);
                byte[] content;
                try
                {
                    content = File.ReadAllBytes(filePath);
                }
                catch (IOException e)
                {
                    Console.WriteLine($"读取文件 {filePath} 时出错: {e.Message}");
                    continue;
                }

                int index = 0;
                int? currentHeaderStart = null;
                int fileCount = 1;

                while (index < content.Length)
                {
                    int headerStartIndex = FindBytes(content, index, ADX_SIG_BYTES);
                    if (headerStartIndex == -1)
                    {
                        if (currentHeaderStart.HasValue)
                        {
                            var searchRange = new byte[content.Length - currentHeaderStart.Value];
                            Array.Copy(content, currentHeaderStart.Value, searchRange, 0, searchRange.Length);
                            extractedFiles.Add((fileName, searchRange, fileCount));
                            fileCount++;
                        }
                        break;
                    }

                    int checkLength = Math.Min(10, content.Length - headerStartIndex);
                    var checkSegment = new byte[checkLength];
                    Array.Copy(content, headerStartIndex, checkSegment, 0, checkLength);

                    if (ContainsBytes(checkSegment, FIXED_SEQUENCES[0]) ||
                        ContainsBytes(checkSegment, FIXED_SEQUENCES[1]))
                    {
                        int nextHeaderIndex = FindBytes(content, headerStartIndex + 1, ADX_SIG_BYTES);
                        if (!currentHeaderStart.HasValue)
                        {
                            currentHeaderStart = headerStartIndex;
                        }
                        else
                        {
                            var rangeLength = headerStartIndex - currentHeaderStart.Value;
                            var searchRange = new byte[rangeLength];
                            Array.Copy(content, currentHeaderStart.Value, searchRange, 0, rangeLength);

                            if (ContainsBytes(searchRange, CRI_COPYRIGHT_BYTES))
                            {
                                extractedFiles.Add((fileName, searchRange, fileCount));
                                fileCount++;
                            }
                            currentHeaderStart = headerStartIndex;
                        }
                    }

                    index = headerStartIndex + 1;
                }
            }

            return extractedFiles;
        }

        public void WriteExtractedFiles(List<(string FileName, byte[] Content, int Count)> extractedFiles, string sourceFolder)
        {
            foreach (var (fileName, fileContent, count) in extractedFiles)
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                string outputFilePath;
                if (count == 1)
                {
                    outputFilePath = Path.Combine(sourceFolder, $"{fileNameWithoutExtension}.adx");
                }
                else
                {
                    outputFilePath = Path.Combine(sourceFolder, $"{fileNameWithoutExtension}_{count}.adx");
                }
                try
                {
                    File.WriteAllBytes(outputFilePath, fileContent);
                    Console.WriteLine($"已提取文件: {outputFilePath}");
                }
                catch (IOException e)
                {
                    Console.WriteLine($"写入文件 {outputFilePath} 时出错: {e.Message}");
                }
            }
        }
    }

    class Program
    {
        static void Main()
        {
            Console.Write("请输入要遍历的文件夹路径: ");
            string? inputPath = Console.ReadLine();
            if (string.IsNullOrEmpty(inputPath))
            {
                Console.WriteLine("输入的路径无效，请重新输入。");
                return;
            }

            AdxExtractStruct extractStruct = new AdxExtractStruct
            {
                SourceFolderPath = inputPath
            };

            AdxExtractor extractor = new AdxExtractor();
            var extractedFiles = extractor.ExtractAdxFiles(extractStruct);
            extractor.WriteExtractedFiles(extractedFiles, extractStruct.SourceFolderPath);

            Console.WriteLine($"共提取出 {extractedFiles.Count} 个符合条件的文件片段。");
        }
    }
}
