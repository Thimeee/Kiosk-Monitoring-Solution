using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.DTO
{
    public class FolderNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public List<FileNode> Files { get; set; } = new();
        public List<FolderNode> SubFolders { get; set; } = new();
        public bool IsExpanded { get; set; } = false;
    }


    public class FileNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public long SizeBytes { get; set; }
        public string SizeFormatted => FormatSize(SizeBytes);

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024 * 1024 * 1024.0):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024 * 1024.0):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        public static implicit operator FileNode(FolderNode v)
        {
            throw new NotImplementedException();
        }
    }
}
