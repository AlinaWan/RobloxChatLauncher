namespace RobloxChatLauncher.Models
{
    public class AreaData
    {
        public long PlaceId
        {
            get; set;
        }
        public string? JobId
        {
            get; set;
        }
        public string? MachineAddress
        {
            get; set;
        }
        public long UniverseId
        {
            get; set;
        }
        public long UserId
        {
            get; set;
        }
        public ServerType ServerType
        {
            get; set;
        }
        public bool IsTeleport
        {
            get; set;
        }
        public string? RPCLaunchData
        {
            get; set;
        }
        public DateTime TimeJoined
        {
            get; set;
        }
        public DateTime TimeLeft
        {
            get; set;
        }
        public string? AccessCode
        {
            get; set;
        }

        public override string ToString()
        {
            return $"PlaceId={PlaceId}, JobId={JobId}, MachineAddress={MachineAddress}, UniverseId={UniverseId}, UserId={UserId}, ServerType={ServerType}";
        }
    }
}
