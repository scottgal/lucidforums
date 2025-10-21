using LucidForums.Models.Dtos;
using LucidForums.Models.Entities;
using LucidForums.Models.ViewModels;
using Mapster;

namespace LucidForums.Web.Mapping;

// Mapster source-generated mapper
[Mapper]
public interface IAppMapper
{
    // DTO -> VM
    ThreadVm ToThreadVm(ThreadView src);
    MessageVm ToMessageVm(MessageView src);

    // Entities -> VMs
    ThreadSummaryVm ToThreadSummaryVm(ForumThread src);
    IEnumerable<ThreadSummaryVm> ToThreadSummaryVms(IEnumerable<ForumThread> src);
    IEnumerable<ForumListItemVm> ToForumListItemVms(IEnumerable<Forum> src);

    // Charter
    CharterListItemVm ToCharterListItemVm(Charter src);
    IEnumerable<CharterListItemVm> ToCharterListItemVms(IEnumerable<Charter> src);
    CharterDetailsVm ToCharterDetailsVm(Charter src);
}
