using LucidForums.Models.Dtos;
using LucidForums.Models.Entities;
using LucidForums.Models.ViewModels;
using Mapster;

namespace LucidForums.Web.Mapping;

public static class MapsterRegistration
{
    public static void Register(TypeAdapterConfig config)
    {
        // Entity -> VM
        config.NewConfig<Forum, ForumListItemVm>();

        config.NewConfig<ForumThread, ThreadSummaryVm>()
            .Map(dest => dest.Id, src => src.Id)
            .Map(dest => dest.Title, src => src.Title)
            .Map(dest => dest.CreatedAtUtc, src => src.CreatedAtUtc)
            .Map(dest => dest.AuthorId, src => src.CreatedById)
            .Map(dest => dest.ForumId, src => src.ForumId);

        // DTO -> VM (use existing ThreadViewService projections)
        config.NewConfig<MessageView, MessageVm>();
        config.NewConfig<ThreadView, ThreadVm>()
            .Map(dest => dest.Messages, src => src.Messages);

        // Commands -> Entities
        config.NewConfig<CreateThreadVm, (string Title, string Content)>()
            .Map(dest => dest.Title, src => src.Title)
            .Map(dest => dest.Content, src => src.Content);

        config.NewConfig<ReplyVm, (Guid? ParentId, string Content)>()
            .Map(dest => dest.ParentId, src => src.ParentId)
            .Map(dest => dest.Content, src => src.Content);
    }
}
