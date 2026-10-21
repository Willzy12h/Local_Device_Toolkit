namespace LocalDeviceToolkit.Core;
public sealed record EventEvidence(string Channel,string Provider,int EventId,long RecordId,DateTimeOffset TimeUtc,string Resource,IReadOnlyDictionary<string,string> Fields);
