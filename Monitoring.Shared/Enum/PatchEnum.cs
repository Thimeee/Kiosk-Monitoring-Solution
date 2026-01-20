using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Enum
{
    public enum PatchStatus
    {
        INIT,
        IN_PROGRESS,
        SUCCESS,
        FAILED,
        ROLLBACK,
        RESTART,

    }
    public enum PatchRequestType
    {
        SINGLE_BRANCH_PATCH,
        ALL_BRANCH_PATCH,
    }



    public enum PatchStep
    {
        START,
        DOWNLOAD,
        VALIDATE,
        EXTRACT,
        STOP_APP,
        BACKUP,
        UPDATE,
        START_APP,
        VERIFY,
        CLEANUP,
        ROLLBACK,
        COMPLETE,
        ERROR,
        RESTART,

    }
}
