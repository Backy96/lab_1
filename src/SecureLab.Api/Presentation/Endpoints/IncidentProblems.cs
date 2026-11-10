namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentProblems
{
    // Усі відповіді без detail, щоб id або причина відмови не потрапляли в тіло.
    public static IResult NotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult CommentNotFound() => Results.Problem(
        title: "Коментар не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult Forbidden() => Results.Problem(
        title: "Дію заборонено",
        statusCode: StatusCodes.Status403Forbidden);

    public static IResult StateConflict() => Results.Problem(
        title: "Конфлікт стану інциденту",
        statusCode: StatusCodes.Status409Conflict);
}