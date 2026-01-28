using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Monitoring.Shared.Enum;

namespace SFTPService.Helper
{
    public class CDKApplctionStatusService
    {
        private readonly string _connectionString;
        private readonly LoggerService _log;

        public CDKApplctionStatusService(IConfiguration config, LoggerService log)
        {
            _connectionString = config.GetConnectionString("BranchDb");
            _log = log;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await _log.WriteLog("DB Connection", "Connection successful", 1);
                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLog("DB Connection Error", $"Failed to connect: {ex.Message}", 3);
                return false;
            }
        }


        public async Task<List<BranchStatusDto>> GetAllBranchStatusAsync()
        {
            var list = new List<BranchStatusDto>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string sql = "SELECT IS_MAINTENANCE_WINDOW, CDK_IN_SERVICE FROM CDK_LOCAL_SETTINGS";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string cdkInService = reader.GetString(1);

                list.Add(new BranchStatusDto
                {
                    IsMaintenanceMood = (CDKErrorStatus)reader.GetInt32(0),
                    CDKINWork = cdkInService == "Y" ? CDKWorkStatus.WORKING : CDKWorkStatus.NOT_WORKING
                });
            }

            return list;
        }



        public class BranchStatusDto
        {
            public CDKErrorStatus IsMaintenanceMood { get; set; }
            public CDKWorkStatus CDKINWork { get; set; }
        }


    }
}
