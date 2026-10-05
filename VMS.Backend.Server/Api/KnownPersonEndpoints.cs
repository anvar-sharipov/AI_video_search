using Microsoft.EntityFrameworkCore;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Domain;
using VMS.Core.Security;
using VMS.MetadataIndexer.Detection;

namespace VMS.Backend.Server.Api;

/// <summary>
/// Enrolls named people for face-based identification — see KnownPersonMatcher for how
/// DetectionWorker uses these to tag live/archived "person" detections with a Name, which is
/// what makes searching by a person's name (rather than an arbitrary reference photo) possible.
/// </summary>
public static class KnownPersonEndpoints
{
    public static void MapKnownPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/known-persons").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IAccessControlManager accessControl, VmsDbContext db) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageKnownPersons);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var people = await db.KnownPersons.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            return Results.Ok(people.Select(p => new KnownPersonDto(p.Id, p.Name, p.EnrolledAt)));
        });

        // Multipart upload (name + reference photo), same shape as POST /api/search/by-face:
        // save to a temp file, run it through the same IFaceEmbedder, persist the embedding.
        group.MapPost("/", async (
            HttpRequest request,
            HttpContext http,
            IAccessControlManager accessControl,
            IFaceEmbedder faceEmbedder,
            IKnownPersonMatcher knownPersonMatcher,
            VmsDbContext db,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageKnownPersons);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var form = await request.ReadFormAsync();
            var name = form["name"].ToString();
            var photo = form.Files["photo"];

            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest("Name is required.");
            }

            if (photo is null)
            {
                return Results.BadRequest("A reference photo is required.");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"known-person-{Guid.NewGuid():N}{Path.GetExtension(photo.FileName)}");
            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await photo.CopyToAsync(stream);
                }

                var embedding = await faceEmbedder.TryGetEmbeddingAsync(tempPath, cropBox: null);
                if (embedding is null)
                {
                    return Results.BadRequest("No face was found in the uploaded photo.");
                }

                var person = new KnownPerson { Name = name.Trim(), FaceEmbedding = embedding };
                db.KnownPersons.Add(person);
                await db.SaveChangesAsync();
                knownPersonMatcher.Invalidate();

                await auditLogger.AppendAsync(new AuditLogEntry
                {
                    UserId = http.User.GetUserId(),
                    Username = http.User.Identity?.Name ?? "unknown",
                    Action = "EnrollKnownPerson",
                    TargetType = nameof(KnownPerson),
                    TargetId = person.Id.ToString(),
                    Details = $"name={person.Name}"
                });

                return Results.Ok(new KnownPersonDto(person.Id, person.Name, person.EnrolledAt));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAccessControlManager accessControl,
            IKnownPersonMatcher knownPersonMatcher,
            VmsDbContext db,
            IAuditLogger auditLogger) =>
        {
            try
            {
                accessControl.Authorize(http.User, Permission.ManageKnownPersons);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }

            var person = await db.KnownPersons.FindAsync(id);
            if (person is null)
            {
                return Results.NotFound();
            }

            db.KnownPersons.Remove(person);
            await db.SaveChangesAsync();
            knownPersonMatcher.Invalidate();

            await auditLogger.AppendAsync(new AuditLogEntry
            {
                UserId = http.User.GetUserId(),
                Username = http.User.Identity?.Name ?? "unknown",
                Action = "DeleteKnownPerson",
                TargetType = nameof(KnownPerson),
                TargetId = person.Id.ToString(),
                Details = $"name={person.Name}"
            });

            return Results.NoContent();
        });
    }
}
