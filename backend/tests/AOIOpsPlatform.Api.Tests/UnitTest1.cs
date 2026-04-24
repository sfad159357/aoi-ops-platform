using AOIOpsPlatform.Api.Controllers;
using AOIOpsPlatform.Domain.Entities;
using AOIOpsPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AOIOpsPlatform.Api.Tests;

public sealed class LotsControllerTests
{
    [Fact]
    public async Task List_returns_lots_ordered_by_createdAt_desc()
    {
        // 為什麼要寫這個測試（給新手看的）：
        // - W02 的第一個交付是 lots list API（/api/lots）。
        // - 如果 list 的排序不固定，前端畫面會「每次刷新順序不同」，新手會以為資料壞掉。
        // - 這個測試用 InMemory DB 快速驗證「排序規則」正確，不需要真的啟 Postgres。

        var options = new DbContextOptionsBuilder<AoiOpsDbContext>()
            .UseInMemoryDatabase(databaseName: $"aoiops-tests-{Guid.NewGuid()}")
            .Options;

        await using var db = new AoiOpsDbContext(options);

        var older = new Lot
        {
            Id = Guid.NewGuid(),
            LotNo = "LOT-OLD",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var newer = new Lot
        {
            Id = Guid.NewGuid(),
            LotNo = "LOT-NEW",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        db.Lots.AddRange(older, newer);
        await db.SaveChangesAsync();

        var controller = new LotsController(db);

        var result = await controller.List(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<LotsController.LotListItemDto>>(ok.Value);

        Assert.Equal(2, items.Count);
        Assert.Equal("LOT-NEW", items[0].LotNo);
        Assert.Equal("LOT-OLD", items[1].LotNo);
    }
}