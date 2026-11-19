namespace MaintTrack.Infrastructure.MachineParameterPhotos;

/// <summary>
/// Links an existing photo to a catalog parameter only when the photo's
/// machine belongs to the same tenant and <c>machines.name</c> equals the
/// parameter <c>name_key</c>. Image URLs and machine ids are left unchanged.
/// Portable across PostgreSQL and SQLite.
/// </summary>
public static class ParameterPhotoLegacyLinker
{
    public const string LinkSql =
        """
        UPDATE machine_parameter_photos
        SET parameter_id = (
            SELECT parameter.id
            FROM parameter_photo_parameters AS parameter
            INNER JOIN machines AS machine
                ON machine.tenant_id = parameter.tenant_id
               AND machine.name = parameter.name_key
            WHERE machine.id = machine_parameter_photos.machine_id
              AND parameter.tenant_id = machine_parameter_photos.tenant_id
            LIMIT 1
        )
        WHERE parameter_id IS NULL
          AND machine_id IS NOT NULL
          AND EXISTS (
            SELECT 1
            FROM parameter_photo_parameters AS parameter
            INNER JOIN machines AS machine
                ON machine.tenant_id = parameter.tenant_id
               AND machine.name = parameter.name_key
            WHERE machine.id = machine_parameter_photos.machine_id
              AND parameter.tenant_id = machine_parameter_photos.tenant_id
          );
        """;
}
