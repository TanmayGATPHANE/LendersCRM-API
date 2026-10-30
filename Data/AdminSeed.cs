namespace AMS.Data;

/** Static dev-seed credentials for the FIRST admin login. The hash is a
    real BCrypt hash of "admin123", pinned as a constant so HasData stays
    deterministic (a dynamic HashPassword call makes the EF model change
    on every run — EF refuses database updates in that state). Rotate the
    password from the Users page after the first sign-in. */
public static class AdminSeed
{
    public const string Email = "admin@lenders.in";
    public const string Password = "admin123";
    /// BCrypt("admin123"), work factor 11.
    public const string PasswordHash = "$2a$11$hvHfLB5KAa.loAm1KdZ3feWxgLOEjC0P5T/K5QuAbhP3rR0U8nrSO";
}