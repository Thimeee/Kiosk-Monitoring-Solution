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
        SCHEDULE,


    }
    public enum PatchSchedulStatus
    {
        NOT_SCHEDULE,
        IS_SCHEDULE,

    }
    public enum PatchRequestType
    {
        SINGLE_BRANCH_PATCH,
        ALL_BRANCH_PATCH,
    }

    public enum PatchIsFinalized
    {
        NOT_FINALIZED,
        IS_FINALIZED,
    }
    public enum PatchIsDownload
    {
        NOT_DOWNLOAD,
        DOWNLOAD_IN_PROGRESS,
        IS_DOWNLOAD,
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
        WATTING,

    }
}
