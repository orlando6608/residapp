using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class ResidentTests
{
    private static Resident Build(string displayName, DateOnly birthDate) => new()
    {
        Id = ResidentId.New(),
        CenterId = CenterId.New(),
        UnitId = UnitId.New(),
        DisplayName = displayName,
        BirthDate = birthDate,
        DocumentedSex = DocumentedSexCode.Unknown,
        Status = ResidentStatus.Active,
    };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_Blank_ThrowsDomainValidation(string blank)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Build(blank, DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.Equal("RESIDENT_CREATE_INPUT_INVALID", ex.Code);
    }

    [Fact]
    public void DisplayName_WithSurroundingWhitespace_IsTrimmed()
    {
        var resident = Build("  Ana García  ", DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal("Ana García", resident.DisplayName);
    }

    [Fact]
    public void BirthDate_InTheFuture_ThrowsDomainValidation()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var ex = Assert.Throws<DomainValidationException>(() => Build("Residente", future));
        Assert.Equal("RESIDENT_CREATE_INPUT_INVALID", ex.Code);
    }

    [Fact]
    public void BirthDate_Today_IsAccepted()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var resident = Build("Residente", today);

        Assert.Equal(today, resident.BirthDate);
    }
}
