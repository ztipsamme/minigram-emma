public record Image(
    string Id,
    string Name,
    string Caption,
    List<string> Tags,
    string Url
);

public record NewImage(
    string Name,
    string Caption,
    List<string>? Tags,
    string Url
);

public record ImageUpdate(
    string? Caption,
    List<string>? Tags
);

public record ImageDTO(
    string? Id,
    string Name,
    string Caption,
    List<string> Tags,
    string Url
);