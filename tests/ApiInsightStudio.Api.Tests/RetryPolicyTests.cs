using ApiInsightStudio.Api.Notifications;

namespace ApiInsightStudio.Api.Tests;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    public void Bekleme_her_basarisizlikta_ikiye_katlanir(int failedAttempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.NextDelay(failedAttempts));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(100)]
    public void Hak_bitince_bekleme_yoktur_is_olur(int failedAttempts)
    {
        Assert.Null(RetryPolicy.NextDelay(failedAttempts));
    }

    [Fact]
    public void Azami_deneme_5tir()
    {
        Assert.Equal(5, RetryPolicy.MaxAttempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Basarisizlik_sayisi_en_az_birdir(int failedAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.NextDelay(failedAttempts));
    }
}
