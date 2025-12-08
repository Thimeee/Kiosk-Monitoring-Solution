namespace MonitoringBackend.Helper
{
    public class CreateUniqId
    {

        public string GenarateUniqID(List<string> parm)
        {
            // Join all list items into a single string separated by underscore
            string listPart = string.Join("_", parm);

            // Append timestamp + GUID
            string timeAndGuid = $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}";

            // Final unique ID
            string jobUid = $"{listPart}_{timeAndGuid}";

            return jobUid;
        }



    }
}
