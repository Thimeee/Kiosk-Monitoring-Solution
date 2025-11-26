
using Monitoring.Shared.DTO;

namespace SFTPService.Helper
{


    public class GetFolderStructure
    {
        public async Task<FolderNode> GetFolderStructureRootAsync(string rootFolder, bool expandRoot = true)
        {
            if (!Directory.Exists(rootFolder))
                throw new DirectoryNotFoundException($"Folder not found: {rootFolder}");

            return await BuildFolderNodeAsync(rootFolder, expandRoot);
        }

        private async Task<FolderNode> BuildFolderNodeAsync(string path, bool expandFolder = false)
        {
            return await Task.Run(async () =>
            {
                var node = new FolderNode
                {
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    //IsExpanded = expandFolder   // <-- set expanded state
                };

                try
                {
                    // Get all files in this folder
                    foreach (var filePath in Directory.GetFiles(path))
                    {
                        var fileInfo = new FileInfo(filePath);
                        node.Files.Add(new FileNode
                        {
                            Name = Path.GetFileName(filePath),
                            FullPath = filePath,
                            SizeBytes = fileInfo.Length
                        });
                    }

                    // Recursively process subfolders
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        var childNode = await BuildFolderNodeAsync(dir); // default collapsed
                        node.SubFolders.Add(childNode);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing {path}: {ex.Message}");
                }

                return node;
            });
        }

    }




}
