namespace DeepLead.Core.Contracts;

public sealed record CountryDto(string Iso2, string Name, string PhoneCode);

public sealed record CityDto(int Id, string Name, string? Region, int? Population, bool IsUserAdded);
