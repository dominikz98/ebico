namespace EBICO.Suite.Components.Stammdaten;

/// <summary>
/// The editing mode of a master-data management component (issue #53): whether it currently shows
/// only its list, a create form, or an edit form.
/// </summary>
public enum FormMode
{
    /// <summary>No form open; only the list is shown.</summary>
    None,

    /// <summary>The create form is open.</summary>
    Create,

    /// <summary>The edit form is open for an existing entry.</summary>
    Edit,
}
