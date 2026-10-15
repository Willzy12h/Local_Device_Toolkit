using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Windows.Inspection;
public static class ScriptStageIdentity
{
 public static string For(ScriptManifest manifest)
 {
  if(manifest.SchemaVersion!=1||manifest.Scripts is null)throw new InvalidDataException("Unsupported script manifest.");
  return CanonicalJson.Digest(new {manifest.SchemaVersion,Scripts=manifest.Scripts.Where(p=>p.ReadOnly).OrderBy(p=>p.Id,StringComparer.Ordinal).Select(p=>new{p.Id,p.Path,p.Sha256}).ToArray()});
 }
}
