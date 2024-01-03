namespace OneDriveRipper
{
    public class UserSecretPrototype
    {
        public string AppId { get; set; }
        public string Scopes { get; set; }
    }

    public class UserSecret
    {
        public string AppId { get; set; }
        public string[] Scopes { get; set; }
    }
}