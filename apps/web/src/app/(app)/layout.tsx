import { UserButton } from "@clerk/nextjs";
import { auth } from "@clerk/nextjs/server";

export default async function AppLayout({ children }: LayoutProps<"/">) {
  await auth.protect();

  return (
    <>
      <header className="flex justify-end gap-4 p-4">
        <UserButton />
      </header>
      {children}
    </>
  );
}
