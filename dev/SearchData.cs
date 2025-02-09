// SearchData.cs

public class SearchData : IEquatable<SearchData>
{
    public string No { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string Work { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string BizNo { get; set; } = string.Empty;
    public string BizDate { get; set; } = string.Empty;
    public string Inst { get; set; } = string.Empty;
    public string Demand { get; set; } = string.Empty;
    public string NoticeDate { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Amt { get; set; } = string.Empty;
    public string RefNo { get; set; } = string.Empty;

    public bool Equals(SearchData? other)
    {
        if (other is null) return false;
        return (No == other.No && Step == other.Step && Work == other.Work && Title == other.Title
            && BizNo == other.BizNo && BizDate == other.BizDate && Inst == other.Inst
            && Demand == other.Demand && NoticeDate == other.NoticeDate
            && ContractType == other.ContractType && Method == other.Method
            && Amt == other.Amt && RefNo == other.RefNo);
    }

    public override bool Equals(object? obj) => Equals(obj as SearchData);

    public override int GetHashCode()
    {
        return (No + Title + BizNo + RefNo).GetHashCode();
    }
}
