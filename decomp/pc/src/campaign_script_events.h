/* UObject's fifth virtual method consumes the script function, a parameter
   block and an optional result pointer on the stack. The two prototypes
   below describe the same x86 call, with either a null or live block.
   A positive float zero or one-word aggregate is stack-passed by the VC 7.1
   fastcall ABI and leaves ECX available for the UObject receiver. */
struct ScriptObject;
struct ScriptFunction;
typedef struct { struct ScriptFunction *value; } FunctionReference;
typedef struct { unsigned int index; } ScriptEventName;
typedef struct { void *address; } ParameterReference;
struct ScriptObjectMethods {
    void *earlier_methods[4];
    union {
        void (__fastcall *empty)(struct ScriptObject *, FunctionReference, float, float);
        void (__fastcall *parameters)(struct ScriptObject *, FunctionReference, ParameterReference, float);
    } process_event;
};
struct ScriptObject { const struct ScriptObjectMethods *methods; };
extern FunctionReference __fastcall find_script_function(struct ScriptObject *, ScriptEventName, float);
