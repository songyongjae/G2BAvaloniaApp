// SearchData.cs

using System;

namespace G2BAvaloniaApp
{
    public class SearchData : IEquatable<SearchData>
    {
        public string No            { get; set; } = "";
        public string Step          { get; set; } = "";
        public string Work          { get; set; } = "";
        public string Title         { get; set; } = "";
        public string BizNo         { get; set; } = "";
        public string BizDate       { get; set; } = "";
        public string Inst          { get; set; } = "";
        public string Demand        { get; set; } = "";
        public string NoticeDate    { get; set; } = "";
        public string ContractType  { get; set; } = "";
        public string Method        { get; set; } = "";
        public string Amt           { get; set; } = "";
        public string RefNo         { get; set; } = "";

        public bool Equals(SearchData? other)
        {
            if (other == null) return false;
            return (No == other.No && Step == other.Step && Work == other.Work && Title == other.Title
                && BizNo == other.BizNo && BizDate == other.BizDate && Inst == other.Inst
                && Demand == other.Demand && NoticeDate == other.NoticeDate
                && ContractType == other.ContractType && Method == other.Method
                && Amt == other.Amt && RefNo == other.RefNo);
        }

        public override bool Equals(object? obj) => Equals(obj as SearchData);
        public override int GetHashCode() => (No + Title + BizNo + RefNo).GetHashCode();
    }
}
