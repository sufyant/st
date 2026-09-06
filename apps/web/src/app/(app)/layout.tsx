import { auth } from "@clerk/nextjs/server";
import { Separator } from "@st/ui/components/ui/separator";
import {
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
} from "@st/ui/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar";
import { DynamicBreadcrumb } from "@/components/dynamic-breadcrumb";

export default async function AppLayout({ children }: LayoutProps<"/">) {
  await auth.protect();

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="flex h-16 shrink-0 items-center gap-2">
          <div className="flex items-center gap-2 px-4">
            <SidebarTrigger className="-ml-1" />
            <Separator
              orientation="vertical"
              className="mr-2 data-[orientation=vertical]:h-4"
            />
            <DynamicBreadcrumb />
          </div>
        </header>
        {children}
      </SidebarInset>
    </SidebarProvider>
  );
}
