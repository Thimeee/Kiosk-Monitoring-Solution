//using Renci.SshNet;

//namespace MonitoringBackend.Helper
//{
//    public class SftpStorageService
//    {
//        private readonly string host = "192.168.1.13";
//        private readonly int port = 22;
//        private readonly string user = "sftpuserfirst";
//        private readonly string pass = "Thimi@1234";

//        public async Task<string> SaveFileAsync(IFormFile file)
//        {
//            try
//            {
//                string remoteDir = "/upload"; // note: use forward slashes
//                string remotePath = $"{remoteDir}/{file.FileName}";

//                using var sftp = new SftpClient(host, port, user, pass);
//                sftp.Connect();

//                // Check if folder exists, create if not
//                if (!sftp.Exists(remoteDir))
//                    sftp.CreateDirectory(remoteDir);

//                using var stream = file.OpenReadStream();
//                sftp.UploadFile(stream, remotePath, true);

//                sftp.Disconnect();
//                return remotePath;
//            }
//            catch (Exception ex)
//            {
//                throw new Exception("SFTP upload failed: " + ex.Message);
//            }

//        }

//    }
//}

using Renci.SshNet;

namespace MonitoringBackend.Helper
{
    public class SftpStorageService : IDisposable
    {
        private readonly SftpClient _sftpClient;

        public SftpStorageService(IConfiguration configuration)
        {
            var host = configuration["Sftp:Host"];
            var port = int.Parse(configuration["Sftp:Port"]);
            var user = configuration["Sftp:User"];
            var pass = configuration["Sftp:Password"];

            _sftpClient = new SftpClient(host, port, user, pass);
        }

        public async Task<string> SaveFileAsync(IFormFile file)
        {
            string remoteDir = "/upload";
            string remotePath = $"{remoteDir}/{file.FileName}";

            return await Task.Run(() =>
            {
                try
                {
                    if (!_sftpClient.IsConnected)
                        _sftpClient.Connect();

                    // create folder if missing
                    if (!_sftpClient.Exists(remoteDir))
                        _sftpClient.CreateDirectory(remoteDir);

                    using var stream = file.OpenReadStream();
                    _sftpClient.UploadFile(stream, remotePath, true);

                    return remoteDir;
                }
                catch (Exception ex)
                {
                    throw new Exception($"SFTP upload failed: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            if (_sftpClient.IsConnected)
                _sftpClient.Disconnect();

            _sftpClient.Dispose();
        }
    }
}
