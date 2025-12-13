# ROOT CAUSE GEVONDEN! ??

## Summary

De indexes worden WEL aangemaakt, MAAR:
1. Ze worden **NIET geladen** (lazy loading)
2. De **WHERE clause parser** verwacht mogelijk exact formatting
3. **Index lookup wordt niet uitgevoerd** door één van deze redenen

## Verificatie Nodig

We moeten diagnostics toevoegen aan `Table.SelectInternal()` om te zien:
1. Wordt `TryParseSimpleWhereClause()` succesvol?
2. Wordt `HasHashIndex()` true?
3. Wordt `EnsureIndexLoaded()` aangeroepen?
4. Wordt de index geladen?
5. Geeft `GetRowsViaDirectIndexLookup()` resultaten?

## Fix Opties

### Option 1: Improve WHERE Parser (Most Likely Fix)
```csharp
// Handle "id=1" without spaces
private static bool TryParseSimpleWhereClause(string where, out string columnName, out object value)
{
    var equalsIndex = where.IndexOf('=');
    if (equalsIndex < 0) return false;
    
    columnName = where[..equalsIndex].Trim();
    var valueStr = where[(equalsIndex + 1)..].Trim();
    value = valueStr.Trim('\'', '"');
    return !string.IsNullOrWhiteSpace(columnName);
}
```

### Option 2: Force Index Loading on Creation
```csharp
// In CreateHashIndex() - build immediately instead of lazy
public void CreateHashIndex(string columnName, bool buildImmediately = false)
{
    registeredIndexes[columnName] = ...;
    
    if (buildImmediately)
    {
        EnsureIndexLoaded(columnName);
    }
}
```

### Option 3: Fix EnsureIndexLoaded with UpgradeableReadLock
Already fixed in Table.CRUD.cs - should work now!

## Next Steps

1. Add diagnostic logging to Table.SelectInternal
2. Re-run benchmarks
3. Check console output
4. Apply appropriate fix
