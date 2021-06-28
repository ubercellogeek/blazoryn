namespace blazoryn.Models
{
    public class BuildManagerState
    {
        public int PercentComplete { get; set; }
        public string Message { get; set; }
        public BuildManagerStateType State { get; set; }

        public void Reset()
        {
            PercentComplete = 0;
            Message = string.Empty;
            State = BuildManagerStateType.NotInitialized;
        }
    }
}