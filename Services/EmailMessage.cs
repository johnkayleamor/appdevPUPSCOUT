using System.Threading.Channels;

namespace appdevPUPSCOUT.Services;

public sealed record EmailMessage(string Type, string To, string Subject, string Body);
