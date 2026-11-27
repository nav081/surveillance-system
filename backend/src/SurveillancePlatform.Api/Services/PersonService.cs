using Microsoft.EntityFrameworkCore;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Domain;
using SurveillancePlatform.Api.Security;

namespace SurveillancePlatform.Api.Services;

public sealed class PersonService(AppDbContext db, ICurrentUser currentUser)
{
    public const int EmbeddingDimension = 512;

    public async Task<List<PersonDto>> ListAsync(CancellationToken ct) =>
        await db.Persons.OrderBy(p => p.DisplayName)
            .Select(p => new PersonDto(p.Id, p.DisplayName, p.CreatedAt))
            .ToListAsync(ct);

    public async Task<PersonDto> CreateAsync(CreatePersonRequest request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId ?? throw new UnauthorizedException("Tenant context is required.");
        var embedding = request.Embedding;

        if (embedding.Length != EmbeddingDimension)
            throw new BadRequestException($"Embedding must contain {EmbeddingDimension} values.");
        if (embedding.Any(v => !float.IsFinite(v)))
            throw new BadRequestException("Embedding contains non-finite values.");

        var person = new Person
        {
            TenantId = tenantId,
            DisplayName = request.DisplayName.Trim(),
            Embedding = Normalize(embedding)
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        return new PersonDto(person.Id, person.DisplayName, person.CreatedAt);
    }

    public async Task DeleteAsync(Guid personId, CancellationToken ct)
    {
        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == personId, ct)
            ?? throw new NotFoundException("Person not found.");
        db.Persons.Remove(person);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Embeddings for the monitoring engine. Bypasses the tenant filter; the engine matches per tenant.</summary>
    public async Task<List<EnrolledPersonDto>> ListEnrolledForEngineAsync(CancellationToken ct) =>
        await db.Persons.IgnoreQueryFilters()
            .Select(p => new EnrolledPersonDto(p.Id, p.TenantId, p.DisplayName, p.Embedding))
            .ToListAsync(ct);

    private static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm < 1e-6f) throw new BadRequestException("Embedding must not be a zero vector.");
        return v.Select(x => x / norm).ToArray();
    }
}

public sealed class AuditLogService(AppDbContext db)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.AuditLogs.AsQueryable();
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.TenantId, a.Actor, a.Action, a.EntityType, a.EntityId, a.Changes, a.IpAddress, a.Timestamp))
            .ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }
}
