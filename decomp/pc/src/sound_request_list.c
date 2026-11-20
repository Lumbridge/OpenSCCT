typedef struct RequestLink {
    void *request;
    struct RequestLink *next;
} RequestLink;
void __stdcall SND_fn_vFreeSnd(void *allocation);

void __cdecl sound_unlink_file_requests(RequestLink **head, const void *request)
{
    RequestLink *current = *head;
    RequestLink *previous = 0;
    while (current) {
        if (current->request == request) {
            RequestLink *removed = current;
            current = current->next;
            SND_fn_vFreeSnd(removed);
            if (previous)
                previous->next = current;
            else
                *head = current;
        } else {
            previous = current;
            current = current->next;
        }
    }
}
