using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Monitoring.Shared.DTO.WorkerServiceConfigDto;
using Monitoring.Shared.Enum;

namespace SFTPService.Service
{
    public class CDKApplctionStatusService
    {
        private readonly string _connectionString;
        private readonly LoggerService _log;

        public CDKApplctionStatusService(IOptions<AppConfig> config, LoggerService log)
        {
            _connectionString = config.Value.ConnectionStrings.BranchDb;
            _log = log;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await _log.WriteLogAsync(LogType.Delay, "SUCCES:DB-Connection", "Connection successful");

                return true;
            }
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Delay, "ERROR:DB-Connection", $"Please Check Connection String");
                await _log.WriteLogAsync(LogType.Exception, "ERROR:DB-Connection", $"Failed to connect: {ex}");

                return false;
            }
        }


        public async Task<List<BranchStatusDto>> GetAllBranchStatusAsync()
        {
            try
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
            catch (Exception ex)
            {
                await _log.WriteLogAsync(LogType.Exception, "ERROR:CDK-Status", $"Failed to get CDK Status: {ex}");
                return null;

            }

        }



        public class BranchStatusDto
        {
            public CDKErrorStatus IsMaintenanceMood { get; set; }
            public CDKWorkStatus CDKINWork { get; set; }
        }


    }
}
