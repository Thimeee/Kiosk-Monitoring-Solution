namespace BranchMonitorFrontEnd.Service.Alert
{
    public class AlertService
    {
        public event Action<string, string, AlertType>? OnAlert;

        public void ShowSuccess(string title, string message)
        {
            OnAlert?.Invoke(title, message, AlertType.Success);
        }

        public void ShowError(string title, string message)
        {
            OnAlert?.Invoke(title, message, AlertType.Error);
        }

        public void ShowWarning(string title, string message)
        {
            OnAlert?.Invoke(title, message, AlertType.Warning);
        }

        public void ShowInfo(string title, string message)
        {
            OnAlert?.Invoke(title, message, AlertType.Info);
        }
    }

    public enum AlertType
    {
        Success,
        Error,
        Warning,
        Info
    }
}
