namespace blazoryn.Models
{
    public enum BuildManagerStateType
    {
        NotInitialized = 1,
        Idle = 2,
        Initializing,
        Building,
        Executing
    }
}