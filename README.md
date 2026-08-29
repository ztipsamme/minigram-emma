# minigram-emma

# Testning

Starta API:t med valfri DEV_TEST_ROLE för lokal testning: `dotnet run --launch-profile <profil>`

Möjliga profiler:

- backend
- backend-admin
- backend-fotograf
- backend-betraktare

## Entra ID

Tre separata Entra ID-användare krävs enligt uppgiften för att representera Admin, Fotograf och Betraktare. Vårt skoltenant tillåter dock inte den inloggade användaren att skapa nya Entra ID-användare, vilket resulterar i Insufficient privileges to complete the operation. Med administratörsbehörighet hade användarna skapats med az ad user create och därefter tilldelats respektive App Role och Storage RBAC-roll.
