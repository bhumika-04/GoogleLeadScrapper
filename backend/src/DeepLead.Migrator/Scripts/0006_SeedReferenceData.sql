-- Minimal reference data for the POC (India / Indore). Full GeoNames city import comes later.

INSERT INTO dbo.Languages (Code, Name, NativeName, IsRtl) VALUES
('en', N'English',   N'English',   0),
('hi', N'Hindi',     N'हिन्दी',      0),
('gu', N'Gujarati',  N'ગુજરાતી',     0),
('mr', N'Marathi',   N'मराठी',       0),
('ta', N'Tamil',     N'தமிழ்',       0),
('te', N'Telugu',    N'తెలుగు',      0),
('kn', N'Kannada',   N'ಕನ್ನಡ',       0),
('bn', N'Bengali',   N'বাংলা',       0),
('ar', N'Arabic',    N'العربية',     1),
('ur', N'Urdu',      N'اردو',        1);

INSERT INTO dbo.Countries (Iso2, Iso3, Name, PhoneCode, GoogleDomain) VALUES
('IN', 'IND', N'India',                '+91',  'google.co.in'),
('AE', 'ARE', N'United Arab Emirates', '+971', 'google.ae');

INSERT INTO dbo.CountryLanguages (CountryIso2, LanguageCode, Priority) VALUES
('IN', 'hi', 1),
('AE', 'ar', 1);

INSERT INTO dbo.Cities (GeoNameId, CountryIso2, Name, AsciiName, Region, Latitude, Longitude) VALUES
(1269743, 'IN', N'Indore', N'Indore', N'Madhya Pradesh', 22.717920, 75.833300);
