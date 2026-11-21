struct DeleteFlags { unsigned value; };

/* The virtual-base displacement occupies the word immediately before the
   incoming subobject. The adjusted receiver and stack flags go to its
   scalar deleting destructor, which returns the object address. */
extern void *__fastcall destroy_adjusted(unsigned char *receiver, struct DeleteFlags flags);

void *__fastcall adjust_and_destroy(unsigned char *subobject, struct DeleteFlags flags)
{
    int displacement = ((const int *)subobject)[-1];
    return destroy_adjusted(subobject - displacement, flags);
}
