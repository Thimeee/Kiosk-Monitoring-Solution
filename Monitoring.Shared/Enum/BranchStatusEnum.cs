using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Monitoring.Shared.Enum
{
    public enum CDKErrorStatus
    {
        OUT_OF_SERVICE,
        IN_SERVICE,

    }
    public enum CDKWorkStatus
    {
        NOT_WORKING, // maps to "N"
        WORKING      // maps to "Y"
    }


    public enum MQTTConnectionStatus
    {
        MANUAL_OFFLINE,
        MANUAL_ONLINE,
        OFFLINE,
        ONLINE,

    }

    public enum DBConnectionStatus
    {

        DISCONNCTED,
        CONNECTED,

    }
}
