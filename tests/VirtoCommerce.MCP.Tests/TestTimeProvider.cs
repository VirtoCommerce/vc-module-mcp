using System;

namespace VirtoCommerce.MCP.Tests;

public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public DateTime UtcNow => _utcNow.UtcDateTime;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
