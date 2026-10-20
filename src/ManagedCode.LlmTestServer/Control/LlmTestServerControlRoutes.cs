namespace ManagedCode.LlmTestServer.Control;

public static class LlmTestServerControlRoutes
{
    public const string Verify = "/admin-api/verify";

    public const string Admin = "/";
    public const string Models = "/admin-api/models";
    public const string Assertions = "/admin-api/assertions";
    public const string Requests = "/admin-api/requests";
    public const string Configure = "/admin-api/configure";
    public const string Conversations = "/admin-api/conversations";
    public const string ExplainMatch = "/admin-api/match/explain";
    public const string Sessions = "/admin-api/sessions";
    public const string Reset = "/admin-api/reset";
}
